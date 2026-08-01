// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Godot.Collections;
using Polytoria.Attributes;
using Polytoria.Client;
using Polytoria.Client.UI.Chat;
using Polytoria.Datamodel.Services;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.DTOs;
using System;
//using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class CharacterModel : Physical
{
	private CharacterModelStateEnum _currentState = CharacterModelStateEnum.Idle;
	private float _currentSpeed = 1;
	private readonly Dictionary<CharacterModelBlendEnum, float> _blendValues = [];
	private Animator? _animator = null!;
	public NPC? _controller = null;
	public Inventory? _inventory = null;
	public Vector3 CharacterVelocity = Vector3.Zero;
	internal Vector3 LastVelocity;
	internal Vector3 ExternalVelocity;
	public CharacterBody3D CharBody3D = null!;
	public const float NameTagHeightMinus = 3f;
	private Vector3 _seatOffset = new(0, 1.7f, 0);
	private float _health = 100;
	private RemoteTransform3D? _toolRemoteTransform;
	private float _maxHealth = 100;
	private float _jumpPower = 36;
	private float _walkSpeed = 16;
	private float _sprintSpeed;
	private float _stamina = 0;
	private float _maxStamina = 3;
	private bool _useStamina = true;
	private float _staminaRegen = 1.2f;
	private float _staminaBurn = 1.2f;
	private const float StepHeight = 1.5f;
	private bool _canMove = true;
	internal bool ClimbDebounce = false;
	internal bool JustFinishedClimbing = false;
	private Tool? _holdingTool;
	private Seat? _sittingIn;
	private bool _keepInventory = false;
	private bool _useHeadTurning = false;
	private bool _useBubbleChat = true;
	private bool _allowAnimationWhileMoving = false;
	public BubbleChat _bubbleChat = null!;
	public const string BubbleChatScene = "res://scenes/client/spatial/chat/bubble_chat.tscn";
	private const float CoyoteTime = 0.15f;
	private Sound? _jumpSound;
	public Dynamic? _head;
	public Dynamic? _toolAttachment;

	public const float ForwardRaycastRange = 1;
	public const float StairForwardRaycastRange = 4;
	protected RayCast3D FootFwdRaycast = null!;
	private bool _lastOnFloorState = false;
	private float _timeSinceGrounded = 0f;
	private bool _coyoteUsed = false;

	private Vector3 _nametagOffset = Vector3.Zero;
	private Vector3 _fixedNametagOffset = new(0, 3, 0);
	private float _nametagVisibleRadius = 40;
	private bool _useNametag = true;
	private Nametag _nametag = null!;

	protected override float PositionSyncThreshold => 0.1f;
	protected override float RotationSyncThreshold => 1f;

	// List of all emotes
	public static readonly string[] EmoteList =
	[
		"wave",
		"dance",
		"helicopter",
		"sit",
		"point",
		"agree",
		"disagree",
		"scream",
		"dance2",
		"disappointed",
	];

	// Oneshot emotes
	public static readonly string[] OneShotEmoteList =
	[
		"wave",
		"point",
		"disagree",
		"agree",
		"scream",
		"disappointed",
	];

	[Editable, ScriptProperty]
	public Dynamic? Head
	{
		get => _head;
		set
		{
			_head = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Dynamic? ToolAttachment
	{
		get => _toolAttachment;
		set
		{
			_toolAttachment = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Sound? JumpSound
	{
		get => _jumpSound;
		set
		{
			_jumpSound = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AllowAnimationWhileMoving
	{
		get => _allowAnimationWhileMoving;
		set
		{
			_allowAnimationWhileMoving = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool KeepInventory
	{
		get => _keepInventory;
		set
		{
			_keepInventory = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseHeadTurning
	{
		get => _useHeadTurning;
		set
		{
			_useHeadTurning = value;
			OnPropertyChanged();
		}
	}

	[SyncVar, ScriptProperty]
	public Tool? HoldingTool
	{
		get
		{
			if (_holdingTool != null && _holdingTool.IsDeleted)
			{
				_holdingTool = null;
			}
			return _holdingTool;
		}
		internal set => _holdingTool = value;
	}

	internal void InternalDetachTool()
	{
		if (_toolRemoteTransform != null && Node.IsInstanceValid(_toolRemoteTransform))
		{
			_toolRemoteTransform?.QueueFree();
		}

		SetBlendValue(CharacterModel.CharacterModelBlendEnum.ToolHoldRight, 0);
	}

	[ScriptMethod, ScriptLegacyMethod("DropTools")]
	public void DropTool()
	{
		if (HoldingTool != null)
		{
			Tool tool = HoldingTool;
			Rpc(nameof(NetDropTool), tool.NetworkedObjectID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private async void NetDropTool(string id)
	{
		Tool? tool = (Tool?)await Root.WaitForNetObjectAsync(id);

		if (tool != null && tool.Droppable)
		{
			tool.Reparent(Root.Environment);
			HoldingTool = null;
			InternalDetachTool();
			tool.InvokeDropped();
		}
	}

	internal override bool TransformNetworkCheck(TransformPayloadDto newTransform)
	{
		// TODO: Make sanity checks here
		return true;
	}

	[ScriptMethod]
	public void LoadAppearance(int userID)
	{
		if (this is PolytorianModel ptm)
		{
			ptm.LoadAppearance(userID, Root.PlayerDefaults.LoadAppearanceTools);
		}
	}

	[Editable, ScriptProperty]
	public float SprintSpeed
	{
		get => _sprintSpeed;
		set
		{
			_sprintSpeed = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public float Stamina
	{
		get => _stamina;
		set
		{
			_stamina = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float MaxStamina
	{
		get => _maxStamina;
		set
		{
			_maxStamina = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("StaminaEnabled")]
	public bool UseStamina
	{
		get => _useStamina;
		set
		{
			_useStamina = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float StaminaRegen
	{
		get => _staminaRegen;
		set
		{
			_staminaRegen = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float StaminaBurn
	{
		get => _staminaBurn;
		set
		{
			_staminaBurn = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 SeatOffset
	{
		get => _seatOffset;
		set
		{
			_seatOffset = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseNametag
	{
		get => _useNametag;
		set
		{
			_useNametag = value;
			_nametag?.UpdateNameTag();
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty]
	public Vector3 NametagOffset
	{
		get => _nametagOffset;
		set
		{
			_nametagOffset = value;
			RecalculateNametagOffset();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float NametagVisibleRadius
	{
		get => _nametagVisibleRadius;
		set
		{
			_nametagVisibleRadius = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Inventory? Inventory
	{
		get => _inventory;
		set
		{
			_inventory = value;
			if (Controller is Player plr && plr.IsLocal)
			{
				Root.CoreUI.CoreUI.Inventory.SetInventory(value);
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool CanMove
	{
		get => _canMove;
		set
		{
			_canMove = value;
			OnPropertyChanged();
		}
	}

	[SyncVar(AllowAuthorWrite = true), ScriptProperty]
	public bool IsClimbing { get; internal set; }

	[SyncVar(AllowAuthorWrite = true), ScriptProperty]
	public Truss? ClimbingTruss { get; internal set; }

	[ScriptProperty, ScriptLegacyProperty("Grounded")]
	public bool IsOnGround => CharBody3D.IsOnFloor();

	[ScriptProperty]
	public bool IsOnCeiling => CharBody3D.IsOnCeiling();

	public override Node CreateGDNode()
	{
		return new CharacterBody3D() { FloorMaxAngle = Mathf.DegToRad(80f) };
	}

	public override void InitGDNode()
	{
		CharBody3D = (CharacterBody3D)GDNode;
		base.InitGDNode();
		CollisionLayers = 2;
		CollisionMask = 3;
	}

	internal void AddStaminaTick(double delta)
	{
		if (!UseStamina) { return; }
		Stamina += (float)(delta * StaminaRegen);
		if (Stamina > MaxStamina)
		{
			Stamina = MaxStamina;
		}
	}

	internal void RemoveStaminaTick(double delta)
	{
		if (!UseStamina) { return; }
		Stamina -= (float)(delta * StaminaBurn);
		if (Stamina < 0)
		{
			Stamina = 0;
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private async void NetSit(string seatID)
	{
		Seat? seat = (Seat?)await Root.WaitForNetObjectAsync(seatID);

		if (seat != null)
		{
			InternalSit(seat);
		}
	}

	private void InternalSit(Seat seat)
	{
		IsSitting = true;
		OverrideNetworkTransform = true;
		SittingIn = seat;
		seat.Occupant = this;
		seat.InvokeSat(this);
		SetBlendValue(CharacterModel.CharacterModelBlendEnum.Sitting, 1);
		Seated.Invoke(seat);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void NetJumpFromSeat()
	{
		if (IsSitting)
		{
			// Unsit the NPC
			IsSitting = false;
			OverrideNetworkTransform = false;

			if (SittingIn != null)
			{
				Seat seat = SittingIn;
				seat.Occupant = null;
				seat.InvokeVacated(this);
				SittingIn = null;
				Unseated.Invoke(seat);
			}

			SetBlendValue(CharacterModel.CharacterModelBlendEnum.Sitting, 0);
		}
	}

	public void WrapToSpawnPoint()
	{
		if (Root.Environment.SpawnPoints.Count > 0)
		{
			Entity spawnpoint = ArrayUtils.GetRandom(Root.Environment.SpawnPoints);
			Position = spawnpoint.Position + new Vector3(0, spawnpoint.Size.Y + 2.0f, 0);
			Rotation = new(0, spawnpoint.Rotation.Y, 0);
		}
		else if (Controller is Player player)
		{
			Position = player.DefaultSpawnLocation;
			Rotation = new(0, 0, 0);
		}

		// Spawn at custom position
#if CREATOR
		if (Controller is Player plr)
		{
			if (Root.Entry != null && Root.Entry.DebugSpawnPos != null)
			{
				if (!plr._spawnedAtCreatorPos)
				{
					plr._spawnedAtCreatorPos = true;
					Position = Root.Entry.DebugSpawnPos.Value;
					Rotation = Vector3.Zero;
				}
			}
		}
#endif
		SendNetTransformReliable();
	}

	public override void Ready()
	{
		if (IsSitting && SittingIn != null)
		{
			InternalSit(SittingIn);
		}

		if (HoldingTool != null)
		{
			InternalAttachTool(HoldingTool);
		}
		RecalculateNametagOffset();
	}

	[Editable, ScriptProperty]
	public float Health
	{
		get => _health;
		set
		{
			if (Controller is Player plr && !plr.IsReady) return;
			float oldHealth = _health;
			_health = value;
			if (_health <= 0 && !IsDead)
			{
				TriggerDead();
			}
			OnPropertyChanged();
			if (_health != oldHealth)
			{
				HealthChanged.Invoke(_health, oldHealth);
			}
		}
	}

	[Editable, ScriptProperty]
	public float MaxHealth
	{
		get => _maxHealth;
		set
		{
			_maxHealth = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float JumpPower
	{
		get => _jumpPower;
		set
		{
			_jumpPower = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float WalkSpeed
	{
		get => _walkSpeed;
		set
		{
			_walkSpeed = value;
			OnPropertyChanged();
		}
	}

	[SyncVar, ScriptProperty]
	public bool IsSitting { get; internal set; } = false;

	[SyncVar, ScriptProperty]
	public bool IsDead { get; internal set; } = false;

	[SyncVar, ScriptProperty]
	public Seat? SittingIn
	{
		get
		{
			if (_sittingIn != null && _sittingIn.IsDeleted)
			{
				_sittingIn = null;
			}
			return _sittingIn;
		}
		internal set => _sittingIn = value;
	}

	[Editable, ScriptProperty]
	public bool UseBubbleChat
	{
		get => _useBubbleChat;
		set
		{
			_useBubbleChat = value;
			_bubbleChat?.Visible = _useBubbleChat;
			OnPropertyChanged();
		}
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);

		if (Root == null) return;
		if (Anchored || IsHidden) return;
		if (!Root.IsLoaded) return;

		// Only enable physics in client mode
		if (Root.SessionType != World.SessionTypeEnum.Client) return;

		if (IsSitting)
		{
			// Add stamina while sitting
			AddStaminaTick(delta);
			if (!Root.Network.IsServer && SittingIn != null)
			{
				Velocity = Vector3.Zero;
				Position = SittingIn.Position + SeatOffset.Y * Up;
				if (!SittingIn.SitDirectionLocked)
				{
					Rotation = new Vector3(SittingIn.Rotation.X, Rotation.Y, SittingIn.Rotation.Z);
				}
				else
				{
					Rotation = SittingIn.Rotation;
				}
				PlayIdle();
			}
			return;
		}

		if (Controller is Player plr)
		{
			if (!plr.IsLocal)
			{
				return;
			}
			if (plr.MovementMode == Player.PlayerMovementModeEnum.Scripted)
			{
				return;
			}
		}

		if (Root.Network.LocalPeerID != NetworkAuthority && ExistInNetwork) return;

		if (CharBody3D != null)
		{
			bool isOnFloor = IsOnGround;
			bool isOnCeiling = IsOnCeiling;

			if (isOnFloor)
			{
				_timeSinceGrounded = 0f;
			}
			else
			{
				_timeSinceGrounded += (float)delta;
			}

			Controller?.Navigate(delta);

			// Apply gravity
			if (!isOnFloor)
			{
				CharacterVelocity.Y += Root.Environment.Gravity.Y * (float)delta;
			}
			else if (isOnFloor && CharacterVelocity.Y < 0)
			{
				// Cancel downward velocity when on floor
				CharacterVelocity.Y = 0;
			}

			// Prevent sticking
			if (isOnCeiling && CharacterVelocity.Y > 0)
			{
				CharacterVelocity.Y = 0;
			}

			UpdateVelocityInternal(CharacterVelocity);
			if (Controller is not Player)
			{
				CharBody3D.Velocity = Velocity;
				CharBody3D.MoveAndSlide();
			}

			if (isOnFloor != _lastOnFloorState)
			{
				_lastOnFloorState = isOnFloor;

				// On floor change
				if (isOnFloor)
				{
					_coyoteUsed = false;
					Landed.Invoke();
				}
				else
				{
					LeftGround.Invoke();
				}
			}

			if (FootFwdRaycast.IsColliding())
			{
				Node collider = (Node)FootFwdRaycast.GetCollider();
				if (collider != null && GetNetObjFromProxy(collider) is Truss truss)
				{
					if (!IsClimbing)
					{
						if (!ClimbDebounce)
						{
							ClimbingTruss = truss;
							IsClimbing = true;
							PlayClimb();
						}

					}
				}
				else
				{
					EndClimb();
				}
			}
			else
			{
				EndClimb();
			}
		}
	}

	/// <summary>
	/// Attach tool to hand
	/// </summary>
	/// <param name="tool"></param>
	private async void InternalAttachTool(Tool tool)
	{
		tool.Holder = this;

		if (_toolRemoteTransform != null && Node.IsInstanceValid(_toolRemoteTransform))
		{
			_toolRemoteTransform.QueueFree();
		}

		_toolRemoteTransform = new()
		{
			UpdatePosition = true,
			UpdateRotation = true,
			UpdateScale = false
		};

		Dynamic attachment = ToolAttachment ?? this;
		attachment.GDNode.AddChild(_toolRemoteTransform, @internal: Node.InternalMode.Back);

		// stick and stones
		// this is needed because GetPath doesn't update when it entered tree
		await Globals.Singleton.WaitFrame();
		_toolRemoteTransform.Position = new Vector3(0, 0, 0);
		_toolRemoteTransform.RotationDegrees = new Vector3(0, -90, -90);
		_toolRemoteTransform.UpdateScale = false;
		_toolRemoteTransform.RemotePath = _toolRemoteTransform.GetPathTo(tool.GDNode);
	}

	[ScriptMethod]
	public void TakeDamage(float dmg)
	{
		Health -= dmg;
	}

	[ScriptMethod]
	public void Heal(float amount)
	{
		Health += amount;
	}

	[ScriptMethod]
	public void UnequipTool()
	{
		if (HoldingTool == null) return;
		Rpc(nameof(NetUnequipTool), HoldingTool.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void NetUnequipTool(string networkID)
	{
		NetworkedObject? netObj = await Root.WaitForNetObjectAsync(networkID);

		if (netObj == null) { return; }

		Tool tool = (Tool)netObj;

		SetBlendValue(CharacterModel.CharacterModelBlendEnum.ToolHoldRight, 0);
		if (Inventory != null)
		{
			tool.Parent = Inventory;
			HoldingTool = null;
			InternalDetachTool();
			tool.InvokeUnequipped();
		}
		else if (tool.Droppable)
		{
			tool.Reparent(Root.Environment);
			HoldingTool = null;
			InternalDetachTool();
			tool.InvokeDropped();
		}
	}

	[ScriptMethod]
	public void EquipTool(Tool tool)
	{
		if (IsDead) return;
		// Check if tool is already held
		if (HoldingTool != null)
		{
			UnequipTool();
		}

		Rpc(nameof(NetEquipTool), tool.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private async void NetEquipTool(string networkID)
	{
		NetworkedObject? netObj = await Root.WaitForNetObjectAsync(networkID);

		if (netObj == null) { return; }

		Tool tool = (Tool)netObj;

		if (tool != null)
		{
			HoldingTool = tool;

			// If is authority, attach the tool
			if (HasAuthority)
			{
				tool.Holder = this;
				tool.Parent = this;
			}

			tool.InvokeEquipped();
		}
	}

	[ScriptMethod]
	public bool TryStepUp()
	{
		if (CharBody3D == null)
		{
			return false;
		}

		if (!CharBody3D.IsOnFloor())
		{
			return false;
		}

		int slideCount = CharBody3D.GetSlideCollisionCount();

		if (slideCount <= 0)
		{
			return false;
		}

		Vector3 desiredVelocity = Velocity;
		Vector3 desiredXZ = new(desiredVelocity.X, 0f, desiredVelocity.Z);
		if (desiredXZ.LengthSquared() < 0.0001f)
		{
			return false;
		}

		float groundY;
		{
			var downHit = new KinematicCollision3D();
			bool hasGround = CharBody3D.TestMove(CharBody3D.GlobalTransform, Vector3.Down * (StepHeight + 0.05f), downHit, 0.001f, true);
			if (!hasGround)
			{
				return false;
			}

			groundY = downHit.GetPosition().Y;
		}

		const float stepSearchOvershoot = 0.05f;

		var spaceState = World.Current!.World3D.DirectSpaceState;

		for (int i = 0; i < slideCount; i++)
		{
			KinematicCollision3D stepTest = CharBody3D.GetSlideCollision(i);
			Vector3 n = stepTest.GetNormal();
			Vector3 p = stepTest.GetPosition();

			if (Mathf.Abs(n.Y) >= 0.01f)
			{
				continue;
			}

			if (!(p.Y - groundY < StepHeight))
			{
				continue;
			}

			float stepHeight = p.Y + StepHeight + 0.0001f;
			Vector3 stepTestInvDir = new Vector3(-n.X, 0, -n.Z).Normalized();
			Vector3 origin = new Vector3(p.X, stepHeight, p.Z) + (stepTestInvDir * stepSearchOvershoot);
			Vector3 direction = Vector3.Down * StepHeight;

			Dictionary result = spaceState.IntersectRay(new PhysicsRayQueryParameters3D()
			{
				From = origin,
				To = origin + direction,
				Exclude = [CharBody3D.GetRid()],
				CollideWithAreas = false,
				CollideWithBodies = true,
			});

			if (result.Count == 0)
			{
				continue;
			}

			Vector3 hitPos = result["position"].AsVector3();

			Vector3 stepUpPoint = new Vector3(p.X, hitPos.Y + 0.01f, p.Z) + (stepTestInvDir * stepSearchOvershoot);
			Vector3 stepUpPointOffset = stepUpPoint - new Vector3(p.X, groundY, p.Z);

			CharBody3D.GlobalPosition += stepUpPointOffset;
			CharBody3D.Velocity = desiredVelocity;

			return true;
		}

		return false;
	}

	[ScriptMethod]
	public virtual void Jump()
	{
		bool canJump = (CharBody3D.IsOnFloor() || (!_coyoteUsed && _timeSinceGrounded <= CoyoteTime)) && JumpPower > 0;
		bool playJumpSound = false;
		if (canJump)
		{
			_coyoteUsed = true;
			CharacterVelocity.Y = JumpPower;
			playJumpSound = true;
			Jumped.Invoke();
		}
		if (IsSitting)
		{
			playJumpSound = true;
			Unsit();
		}
		if (playJumpSound && JumpSound != null && !JumpSound.Playing)
		{
			JumpSound?.Play();
		}
		if (IsClimbing)
		{
			EndClimb();
			ClimbDebounce = true;
		}
	}

	[ScriptMethod]
	public void Respawn()
	{
		if (Controller is Player plr)
		{
			plr.Respawn();
		}
		else
		{
			Health = MaxHealth;
			Anchored = false;
			IsDead = false;

			if (this is PolytorianModel ptmodel)
			{
				ptmodel.StopRagdoll();
			}
			CharacterVelocity = Vector3.Zero;

			OverrideCanCollide = false;
			UpdateCollision();
		}
	}

	[ScriptMethod]
	public void Move(Vector3 velo)
	{
		CharacterVelocity = velo;
		UpdateVelocityInternal(CharacterVelocity);
		CharBody3D.Velocity = Velocity;
		CharBody3D.MoveAndSlide();
		CharacterVelocity = CharBody3D.Velocity;
		UpdateVelocityInternal(CharacterVelocity);
	}

	[ScriptEnum]
	public enum CharacterModelStateEnum
	{
		Idle,
		Walking,
		Running,
		Jumping,
		Climbing
	}

	public enum CharacterModelBlendEnum
	{
		Sitting,
		ToolHoldLeft,
		ToolHoldRight,
		LookX,
		LookY,
	}

	public void TriggerDead()
	{
		if (IsDead) return;
		if (Root.SessionType != World.SessionTypeEnum.Client) return;
		Anchored = true;
		OverrideCanCollide = true;
		OverrideCanCollideTo = false;
		Unsit(false);
		UpdateCollision();

		Animator?.StopAnimation();
		Animator?.StopOneShotAnimation();

		if (this is PolytorianModel ptmodel)
		{
			ptmodel.StartRagdoll(Velocity);
		}
		IsDead = true;
		Died.Invoke();
	}

	[ScriptMethod]
	public void Sit(Seat seat)
	{
		Rpc(nameof(NetSit), seat.NetworkedObjectID);
	}

	[ScriptMethod]
	public void Unsit(bool addForce = true)
	{
		Rpc(nameof(NetJumpFromSeat));

		// Reset rotation
		Rotation = new(0, Rotation.Y, 0);

		if (addForce)
		{
			Position += SeatOffset * 2;
		}
	}

	private void RecalculateNametagOffset()
	{
		if (!_nametag.IsInsideTree()) { return; }
		_nametag.Position = NametagOffset + _fixedNametagOffset;
	}

	[ScriptMethod]
	public void Kill()
	{
		Health = 0;
		RpcId(1, nameof(NetKill));
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetKill()
	{
		Health = 0;
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 Velocity
	{
		get
		{
			return CharacterVelocity;
		}
		set
		{
			if (Controller is Player plr)
			{
				LastVelocity = value;
			}

			CharacterVelocity = value;

			OnPropertyChanged();
		}
	}

	internal void ApplyInternalVelocity(Vector3 velocity)
	{
		UpdateVelocityInternal(velocity);
		CharacterVelocity = velocity;
		OnPropertyChanged(nameof(Velocity));
	}

	internal void EndClimb()
	{
		if (!IsClimbing) { return; }
		IsClimbing = false;
		JustFinishedClimbing = true;
		ClimbingTruss = null;
		SetAnimSpeed(1);
	}

#if CREATOR
	public override void CreatorInserted()
	{
		Root.Insert.InitializeDefaultCharacter(this);
		base.CreatorInserted();
	}
#endif

	[Editable, ScriptProperty]
	public NPC? Controller
	{
		get => _controller;
		set
		{
			if (value is null)
			{
				Controller?._character = null;
				Controller?.OnPropertyChanged();
				_controller = null;
				OnPropertyChanged();
				if (Root.Network.IsServer && ((value?.NetworkAuthority ?? 1) != 1))
				{
					SetNetworkAuthority(null);
					SetNetworkAuthority(1, true);
				}
			}
			else
			{
				value!.Character = this;
			}
		}
	}

	[ScriptProperty, SyncVar(AllowAuthorWrite = true)]
	public CharacterModelStateEnum CurrentState
	{
		get => _currentState;
		set
		{
			_currentState = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar(AllowAuthorWrite = true, Unreliable = true)]
	public float CurrentSpeed
	{
		get => _currentSpeed;
		set
		{
			_currentSpeed = value;
			RecvSpeedValue(value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Animator? Animator
	{
		get
		{
			if (_animator != null && _animator.IsDeleted)
			{
				_animator = null!;
			}
			return _animator;
		}
		set => _animator = value;
	}

	private bool _peerReadySubscribed = false;

	public override void Init()
	{
		base.Init();
		EnsureTouchArea();
		OverridePhysicsProcess = true;

		_bubbleChat = Globals.CreateInstanceFromScene<BubbleChat>(BubbleChatScene);
		_bubbleChat.SetTarget(this.Controller is Player plr ? plr : null);
		_bubbleChat.Visible = _useBubbleChat;
		GDNode.AddChild(_bubbleChat, @internal: Node.InternalMode.Back);
		excludedBoundNodes.Add(_bubbleChat);

		// Create nametag
		_nametag = new()
		{
			Target = this
		};
		GDNode3D.AddChild(_nametag);
		excludedBoundNodes.Add(_nametag);

		FootFwdRaycast = new();
		GDNode3D.AddChild(FootFwdRaycast, false, Node.InternalMode.Front);
		FootFwdRaycast.Position = new Vector3(0, -3, 0);
		FootFwdRaycast.TargetPosition = new Vector3(0, 0, ForwardRaycastRange);

		ChildAdded.Connect(OnChildAdded);
		ChildRemoved.Connect(OnChildRemoved);
		Died.Connect(OnDied);
		Destroying.Connect(OnDestroying);

		RecalculateNametagOffset();

		// Force these to always be on
		SetProcess(true);
		SetPhysicsProcess(true);
		if (Root != null && Root.Network != null && NetworkService.CheckAuthority(Root.Network.LocalPeerID, NetworkAuthority))
		{
			_peerReadySubscribed = true;
			Root.Network.PeerPreInit += OnPeerPreInit;
		}
	}

	public void OnDestroying()
	{
		if (Controller is Player plr) plr?.OnPlayerDied();
		Controller?.Character = null;
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (Controller is Player plr && !plr.IsLocal)
		{
			UpdateTransformTick(delta);
			if (Root.Network.IsServer && !IsSitting)
			{
				CharBody3D.Velocity = LastVelocity;
				CharBody3D.MoveAndSlide();
				LastVelocity = Vector3.Zero;
				ApplyPushForce();
			}
		}
	}

	public void OnDied()
	{
		UnequipTool();
		Velocity = Vector3.Zero;
		if (Controller is Player plr) plr.OnPlayerDied();
	}

	public override void InitOverrides()
	{
		Anchored = false;
	}

	public override void PreDelete()
	{
		ChildAdded.Disconnect(OnChildAdded);
		ChildRemoved.Disconnect(OnChildRemoved);
		Died.Disconnect(OnDied);
		if (_peerReadySubscribed)
		{
			Root.Network.PeerPreInit -= OnPeerPreInit;
		}
		base.PreDelete();
	}

	private void OnPeerPreInit(int id)
	{
		foreach ((CharacterModelBlendEnum blend, float val) in _blendValues)
		{
			RpcId(id, nameof(NetSetBlendValue), (int)blend, val);
		}
	}

	private void OnChildAdded(Instance n)
	{
		if (n is Tool t)
		{
			InternalAttachTool(t);
		}
	}

	private void OnChildRemoved(Instance n)
	{
		if (n is Tool)
		{
			InternalDetachTool();
		}
	}

	internal void ApplyPushForce()
	{
		for (int i = 0; i < CharBody3D.GetSlideCollisionCount(); i++)
		{
			KinematicCollision3D collision = CharBody3D.GetSlideCollision(i);

			if (GetNetObjFromProxy((Node)collision.GetCollider()) is Physical body)
			{
				// Push the rigidbody
				body.ApplyForceFromPlayer(-collision.GetNormal());
			}
		}
	}

	[ScriptProperty]
	public PTSignal Died { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Landed { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<float, float> HealthChanged { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Jumped { get; private set; } = new();

	[ScriptProperty]
	public PTSignal LeftGround { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Seat> Seated { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Seat> Unseated { get; private set; } = new();

	[ScriptMethod]
	public void PlayIdle()
	{
		SetState(CharacterModelStateEnum.Idle);
	}

	[ScriptMethod]
	public void PlayWalk()
	{
		SetState(CharacterModelStateEnum.Walking);
	}

	[ScriptMethod]
	public void PlayRun()
	{
		SetState(CharacterModelStateEnum.Running);
	}

	[ScriptMethod]
	public void PlayJump()
	{
		SetState(CharacterModelStateEnum.Jumping);
	}

	[ScriptMethod]
	public void PlayClimb()
	{
		SetState(CharacterModelStateEnum.Climbing);
	}

	internal void PlayEmote(string emoteName)
	{
		if (IsSitting || IsDead) return;
		if (!EmoteList.Contains(emoteName)) return;
		bool isOneShot = false;
		if (OneShotEmoteList.Contains(emoteName))
		{
			isOneShot = true;
		}

		Animator?.StopAnimation();
		Animator?.StopOneShotAnimation();

		if (isOneShot)
		{
			Animator?.PlayOneShotAnimation("emote_" + emoteName);
		}
		else
		{
			Animator?.PlayAnimation("emote_" + emoteName);
		}
	}

	[ScriptMethod]
	public void SetAnimSpeed(float speed)
	{
		CurrentSpeed = speed;
	}

	[ScriptMethod]
	public void SetState(CharacterModelStateEnum newState)
	{
		if (newState != CurrentState)
		{
			CurrentState = newState;
		}
	}

	public void SetBlendValue(CharacterModelBlendEnum blend, float value)
	{
		InternalSetBlendValue(blend, value);
		if (HasAuthority)
		{
			Rpc(nameof(NetSetBlendValue), (int)blend, value);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetBlendValue(int blendName, float blendValue)
	{
		InternalSetBlendValue((CharacterModelBlendEnum)blendName, blendValue);
	}

	private void InternalSetBlendValue(CharacterModelBlendEnum blendName, float blendValue)
	{
		_blendValues[blendName] = blendValue;
		RecvBlendValue(blendName, blendValue);
	}

	public virtual void RecvBlendValue(CharacterModelBlendEnum blendName, float blendValue) { }
	public virtual void RecvSpeedValue(float speedValue) { }

	public virtual void ApplyCameraModifier(Camera camera) { }

	[ScriptEnum]
	public enum CharacterAttachmentEnum
	{
		Head,
		UpperTorso,
		LowerTorso,
		ShoulderLeft,
		ShoulderRight,
		ElbowLeft,
		ElbowRight,
		HandLeft,
		HandRight,
		LegLeft,
		LegRight,
		KneeLeft,
		KneeRight,
		FootLeft,
		FootRight
	}
}
