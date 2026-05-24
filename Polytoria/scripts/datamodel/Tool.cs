// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Tool : RigidBody
{
	private bool _droppable = true;
	private ImageAsset? _iconImage;
	private NPC? _holder = null;

	private double _dropEquipCooldown;
	private Timer _equipTimer = null!;

	[Editable, ScriptProperty]
	public bool Droppable
	{
		get => _droppable;
		set
		{
			_droppable = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? IconImage
	{
		get => _iconImage;
		set
		{
			_iconImage = value;
			if (_iconImage != null && _iconImage != value)
			{
				_iconImage.ResourceLoaded -= OnToolImageLoaded;
				_iconImage.UnlinkFrom(this);
			}
			OnToolImageLoaded(null);
			_iconImage = value;
			if (_iconImage != null)
			{
				_iconImage.LinkTo(this);
				_iconImage.ResourceLoaded += OnToolImageLoaded;

				if (_iconImage.IsResourceLoaded && _iconImage.Resource != null)
				{
					OnToolImageLoaded(_iconImage.Resource);
				}
				else
				{
					_iconImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.5)]
	public float DropEquipCooldown
	{
		get => (float)_dropEquipCooldown;
		set
		{
			_equipTimer.WaitTime = _dropEquipCooldown = value;
			OnPropertyChanged();
		}
	}

	[SyncVar, ScriptProperty]
	public NPC? Holder
	{
		get
		{
			if (_holder != null && _holder.IsDeleted)
			{
				_holder = null;
			}
			return _holder;
		}
		set
		{
			_holder = value;
			UpdateHandHold();
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public PTSignal Equipped { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Unequipped { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Activated { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Deactivated { get; private set; } = new();

	internal Texture2D? ToolImgTexture = null!;
	internal event Action? ToolImgTextureLoaded;

	private readonly HashSet<Instance> _registeredInstances = [];
	private bool _hasDynChild = false;
	private CollisionShape3D? _toolCollision;

	private void OnToolImageLoaded(Resource? resource)
	{
		ToolImgTexture = (Texture2D?)resource;
		ToolImgTextureLoaded?.Invoke();
	}

	private void OnToolTouched(Physical physical)
	{
		if (physical is NPC npc && _holder == null && Parent is not (Inventory or Player) && _equipTimer.IsStopped())
		{
			npc.EquipTool(this);
		}
	}

	public override void InitGDNode()
	{
		GDNode.AddChild(_equipTimer = new() { OneShot = true }, @internal: Node.InternalMode.Back);
		base.InitGDNode();
	}

	public override void EnterTree()
	{
		base.EnterTree();
		if (Parent is not (Inventory or NPC))
		{
			CanCollide = true;
			Anchored = false;
			_toolCollision = new()
			{
				Shape = new BoxShape3D() { Size = GetBounds().Size },
			};
			GDNode.AddChild(_toolCollision, @internal: Node.InternalMode.Back);
			AddCollisionShape(_toolCollision);
		}
		else
		{
			CanCollide = false;
			Anchored = true;
			RegisterNoMultiMesh(this);
		}
		Root.Input.GodotInputEvent += OnInput;

		// Just in case it were reparented while still having a holder
		if (Parent is not NPC && Holder != null)
		{
			Holder.InternalDetachTool();
			Holder = null;
		}

		if (HasAuthority)
		{
			Touched.Connect(OnToolTouched);
		}

		UpdateHandHold();
		ChildAdded.Connect(ChildAddedA);
	}

	public override void ExitTree()
	{
		UnregisterNoMultiMesh(this);
		ChildAdded.Disconnect(ChildAddedA);
		Root.Input.GodotInputEvent -= OnInput;

		if (_toolCollision != null)
		{
			RemoveCollisionShape(_toolCollision);
			_toolCollision = null;
		}

		if (HasAuthority)
		{
			Touched.Disconnect(OnToolTouched);
		}
		base.ExitTree();
	}

	private void ChildAddedA(Instance _)
	{
		UpdateHandHold();
	}

	private void UpdateHandHold()
	{
		_hasDynChild = Children.Any(i => i is Dynamic);
		if (Holder != null && Holder.Character != null)
		{
			Holder.Character.SetBlendValue(CharacterModel.CharacterModelBlendEnum.ToolHoldRight, _hasDynChild ? 1 : 0);
		}
	}

	public override void PreDelete()
	{
		_registeredInstances.Clear();
		base.PreDelete();
	}

	public void OnInput(InputEvent @event)
	{
		if (Holder != Root.Players.LocalPlayer) return;
		if (@event.IsActionPressed("activate"))
		{
			Activate();
		}
		if (@event.IsActionReleased("activate"))
		{
			Deactivate();
		}
	}

	[ScriptMethod]
	public void Activate()
	{
		if (!Root.Network.IsServer)
		{
			Activated.Invoke();
		}
		RpcId(1, nameof(NetRecvActivate));
	}

	[ScriptMethod]
	public void Deactivate()
	{
		if (!Root.Network.IsServer)
		{
			Deactivated.Invoke();
		}
		RpcId(1, nameof(NetRecvDeactivate));
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void NetRecvActivate()
	{
		if (Holder == null) return;

		// Only allow from the holder
		if (Holder is Player plr && RemoteSenderId != plr.PeerID) return;

		// Only allow from server if is NPC
		if (Holder is not Player && Holder is not null && RemoteSenderId != 1) return;
		Activated.Invoke();
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void NetRecvDeactivate()
	{
		if (Holder == null) return;

		// Only allow from the holder
		if (Holder is Player plr && RemoteSenderId != plr.PeerID) return;

		// Only allow from server if is NPC
		if (Holder is not Player && Holder is not null && RemoteSenderId != 1) return;
		Deactivated.Invoke();
	}

	private void RegisterNoMultiMesh(Instance instance)
	{
		// Only register if not already registered
		if (_registeredInstances.Add(instance))
		{
			if (instance is Part p)
			{
				p.OverrideNoMultiMesh = true;
			}
			if (instance is Physical e)
			{
				e.OverrideCanCollideTo = false;
				e.OverrideCanCollide = true;
				e.UpdateCollision();
			}
			foreach (Instance item in instance.GetChildren())
			{
				RegisterNoMultiMesh(item);
			}

			instance.ChildAdded.Connect(RegisterNoMultiMesh);
			instance.ChildRemoved.Connect(UnregisterNoMultiMesh);
		}
	}

	private void UnregisterNoMultiMesh(Instance instance)
	{
		// Only unregister if it was registered
		if (_registeredInstances.Remove(instance))
		{
			if (instance is Part p)
			{
				p.OverrideNoMultiMesh = false;
			}
			if (instance is Physical e)
			{
				e.OverrideCanCollideTo = false;
				e.OverrideCanCollide = false;
				e.UpdateCollision();
			}

			foreach (Instance item in instance.GetChildren())
			{
				UnregisterNoMultiMesh(item);
			}

			instance.ChildAdded.Disconnect(RegisterNoMultiMesh);
			instance.ChildRemoved.Disconnect(UnregisterNoMultiMesh);
		}
	}

	[ScriptMethod, ScriptLegacyMethod("Play")]
	public void PlayAnimation(string animationName)
	{
		Holder?.Character?.Animator?.PlayOneShotAnimation(animationName);
	}

	internal void InvokeEquipped()
	{
		// NOTE: HACKY!!!
		// call deferred to let scripts run first
		PT.CallDeferred(() =>
		{
			PT.CallDeferred(() =>
			{
				Equipped?.Invoke();
			});
		});
	}

	protected override void ApplyFreeze(bool to)
	{
		GDRigidBody.Freeze = to;
		base.ApplyFreeze(to);
	}

	internal void InvokeUnequipped()
	{
		Holder = null;
		Unequipped?.Invoke();
	}

	internal void InvokeDropped()
	{
		if (_dropEquipCooldown > 0.05) _equipTimer.Start(_dropEquipCooldown);
	}
}
