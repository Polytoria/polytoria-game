// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using static Polytoria.Datamodel.Environment;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Grabbable : Instance
{
	private bool _dragging = false;
	private Physical? _parent = null!;

	private float _force;
	private float _maxRange;
	private float _maxGrabbableRange;
	private bool _useDragForce;
	private Player? _dragger;
	private GrabbablePermissionModeEnum _permissionMode = GrabbablePermissionModeEnum.Everyone;

	[Editable, ScriptProperty, DefaultValue(10)]
	public float Force
	{
		get => _force;
		set
		{
			_force = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(8)]
	public float MaxRange
	{
		get => _maxRange;
		set
		{
			_maxRange = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(12)]
	public float MaxGrabbableRange
	{
		get => _maxGrabbableRange;
		set
		{
			_maxGrabbableRange = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool UseDragForce
	{
		get => _useDragForce;
		set
		{
			_useDragForce = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public GrabbablePermissionModeEnum PermissionMode
	{
		get => _permissionMode;
		set
		{
			_permissionMode = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public Player? Dragger => _dragger;
	[ScriptProperty] public PTFunction? PermissionPredicate { get; set; }
	[ScriptProperty] public PTSignal<Player> Grabbed { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> Released { get; private set; } = new();

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_parent = phy;
			phy.Clicked.Connect(OnClicked);
			phy.MouseEnter.Connect(OnMouseEnter);
			phy.MouseExit.Connect(OnMouseExit);
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		_parent?.Clicked.Disconnect(OnClicked);
		_parent?.MouseEnter.Disconnect(OnMouseEnter);
		_parent?.MouseExit.Disconnect(OnMouseExit);
		_parent = null;
		base.ExitTree();
	}

	public override void Init()
	{
		base.Init();
		Root.Input.GodotInputEvent += OnInput;
		SetPhysicsProcess(true);
	}

	public override void PreDelete()
	{
		Root.Input.GodotInputEvent -= OnInput;
		base.PreDelete();
	}

	private void OnMouseEnter()
	{
		if (!_dragging)
		{
			Root.PlayerGUI.SetCursorShape(Control.CursorShape.Drag);
		}
	}

	private void OnMouseExit()
	{
		if (!_dragging)
		{
			Root.PlayerGUI.SetCursorShape(Control.CursorShape.Arrow);
		}
	}


	public void OnInput(InputEvent @event)
	{
		if (@event.IsActionReleased("activate"))
		{
			if (_dragging)
			{
				ReleaseDrag();
			}
		}
	}

	private async void OnClicked(Player by)
	{
		if (_dragger != null) return;
		if (_parent != null)
		{
			// Check grabbable range
			if ((by.Position - _parent.Position).Length() > MaxGrabbableRange) return;
		}
		if (Root.Network.IsServer)
		{
			// If is server
			if (PermissionMode == GrabbablePermissionModeEnum.Everyone)
			{
				GiveDragTo(by);
			}
			else if (PermissionMode == GrabbablePermissionModeEnum.Scripted)
			{
				if (PermissionPredicate != null)
				{
					object?[] res = await PermissionPredicate.Call(by);
					if (res.Length != 1) return;
					if (res[0] is bool b && b)
					{
						GiveDragTo(by);
					}
				}
			}
		}
		else if (by == Root.Players.LocalPlayer)
		{
			// If is self
			if (PermissionMode == GrabbablePermissionModeEnum.Everyone)
			{
				InternalGiveGrab();
			}
		}
	}

	private void GiveDragTo(Player plr)
	{
		if (_parent == null) return;
		_dragger = plr;
		_parent.SetNetworkAuthority(plr);
		Grabbed.Invoke(plr);
		RpcId(plr.PeerID, nameof(NetGrabDrag));
	}

	private void ReleaseDrag()
	{
		InternalReleaseDrag();
		Root.PlayerGUI.SetCursorShape(Control.CursorShape.Arrow);
		RpcId(1, nameof(NetDispatchReleaseDrag));
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetGrabDrag()
	{
		InternalGiveGrab();
	}

	internal void InternalGiveGrab()
	{
		_dragger = Root.Players.LocalPlayer;
		_dragging = true;
		Grabbed.Invoke(_dragger);
		Root.PlayerGUI.SetCursorShape(Control.CursorShape.CanDrop);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetDispatchReleaseDrag()
	{
		Player? p = Root.Players.GetPlayerFromPeerID(RemoteSenderId);

		if (p == _dragger)
		{
			InternalReleaseDrag();

			// Return authority to server
			_parent?.SetNetworkAuthority(null);

			Rpc(nameof(NetReleaseDrag));
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetReleaseDrag()
	{
		InternalReleaseDrag();
	}

	private void InternalReleaseDrag()
	{
		_dragging = false;
		_dragger = null;
		Released.Invoke();
	}

	public override void PhysicsProcess(double delta)
	{
		if (Parent == null) return;
		if (_dragger == null) return;

		// Set to null when deleted
		if (_dragger.IsDeleted) { _dragger = null; return; }

		// Process drag physics if enabled
		if (UseDragForce)
		{
			if (Parent.GDNode is RigidBody3D rigid3D)
			{
				if (_dragging)
				{
					Viewport viewport = Globals.Singleton.GetViewport();
					Camera3D camera = viewport.GetCamera3D();
					Camera? cam = Root.Environment.CurrentCamera;
					if (cam == null) return;
					Vector2 mousePos = Root.Input.MousePosition;
					Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
					Vector3 rayDir = camera.ProjectRayNormal(mousePos);

					Vector3? targetPos = null;

					if (cam.IsFirstPerson)
					{
						targetPos = rayOrigin + rayDir * MaxRange;
					}
					else
					{
						RayResult? hit = Root.Environment.Raycast(rayOrigin, rayDir, ignoreList: [Parent]);
						if (hit != null)
						{
							targetPos = hit.Value.Position;
						}
					}

					if (targetPos == null) return;

					Vector3 anchorPos = _dragger.Position;
					Vector3 direction = targetPos.Value - anchorPos;
					float distance = direction.Length();

					if (distance > MaxRange)
					{
						targetPos = anchorPos + direction.Normalized() * MaxRange;
					}

					Vector3 moveDirection = targetPos.Value - rigid3D.GlobalPosition;
					rigid3D.LinearVelocity = moveDirection * Force;
				}
			}
		}
		base.PhysicsProcess(delta);
	}

	public enum GrabbablePermissionModeEnum
	{
		None,
		Everyone,
		Scripted
	}
}
