// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Polytoria.Attributes;
using Polytoria.Client;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Datamodel.Resources;
using System.Runtime.CompilerServices;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class NPC : Physical
{
	private const float NavigationDistance = 2f;
	public const float BodyRotateLerp = 10f;
	private const float StepHeight = 1.5f;
	private Tool? _holdingTool;
	private Seat? _sittingIn;
	private CharacterModel? _character;
	private Dynamic? _moveTarget;

	public CharacterBody3D CharBody3D = null!;
	public const float ForwardRaycastRange = 1;
	const float EjectMomentumScale = 0.35f;
	const float SeatExceptionReleaseDelay = 0.3f;
	private Vector3 _seatOffset = new(0, 1.7f, 0);
	private bool _writingSeat = false;
	private readonly List<CollisionObject3D> _seatCollisionExceptions = [];
	private float _seatExceptionReleaseTimer = 0f;
	internal Vector3 EjectMomentum = Vector3.Zero;
	private float _health = 100;
	private RemoteTransform3D? _toolRemoteTransform;
	private float _maxHealth = 100;
	private float _jumpPower = 36;
	private float _walkSpeed = 16;
	private float _coyoteTime = 0.15f;
	private string _displayName = "";
	protected RayCast3D FootFwdRaycast = null!;
	private Sound? _jumpSound;
	private Sound? _fallSound;
	private Sound? _landSound;
	private Sound? _walkSound;
	private bool _lastOnFloorState = false;
	private float _timeSinceGrounded = 0f;
	private bool _coyoteUsed = false;
	private Node3D? _navAgentContainer;
	private NavigationAgent3D? _navAgent;
	internal float ClimbJumpCooldownRemaining = 0f;

	private const float StepDistance = 5.5f;
	private const float FootstepBasePitch = 1f;
	private const float FootstepPitchVariance = 0.25f;
	private static readonly System.Collections.Generic.Dictionary<string, BuiltInAudioAsset.BuiltInAudioPresetEnum> _materialSounds = new()
	{
		{ "Plastic", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepPlastic },
		{ "Brick", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepStone },
		{ "Concrete", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepStone },
		{ "Dirt", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepDirt },
		{ "Fabric", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepFabric },
		{ "Grass", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepGrass },
		{ "Ice", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepIce },
		{ "Metal", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepMetal },
		{ "MetalGrid", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepPlate },
		{ "MetalPlate", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepPlate },
		{ "Planks", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepPlanks },
		{ "Plywood", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepWood },
		{ "RustyIron", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepMetal },
		{ "Sand", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepSand },
		{ "Sandstone", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepStone },
		{ "Snow", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepSand },
		{ "Stone", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepStone },
		{ "Wood", BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepWood }
	};
	private readonly System.Collections.Generic.Dictionary<BuiltInAudioAsset.BuiltInAudioPresetEnum, BuiltInAudioAsset> _footstepAudioCache = [];
	private float _distanceSinceStep = 0f;
	private float _lastFootstepPitch = FootstepBasePitch;
	private bool _lastStepWasLeft = false;
	private BuiltInAudioAsset.BuiltInAudioPresetEnum _currentFootstepPreset = BuiltInAudioAsset.BuiltInAudioPresetEnum.FootstepPlastic;

	private const float PanicFallDelay = 0.5f;
	private const float PanicFallHeight = 75f;
	private float _fallTimer = 0f;

	private Vector3 _nametagOffset = Vector3.Zero;
	private Vector3 _fixedNametagOffset = new(0, 3, 0);
	private float _nametagVisibleRadius = 40;
	private bool _useNametag = true;
	private Nametag _nametag = null!;

	// Pending properties to apply to character
	private Color? _pendingHeadColor;
	private Color? _pendingTorsoColor;
	private Color? _pendingLeftArmColor;
	private Color? _pendingRightArmColor;
	private Color? _pendingLeftLegColor;
	private Color? _pendingRightLegColor;
	private int? _pendingFaceID;

	protected override float PositionSyncThreshold => 0.1f;
	protected override float RotationSyncThreshold => 1f;

	public bool IsPanicFalling { get; private set; } = false;
	internal bool JustJumped { get; private set; } = false;
	internal bool IsClimbing => this is Player plr && plr.IsClimbing;

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 Velocity
	{
		get
		{
			return CharacterVelocity;
		}
		set
		{
			if (this is Player plr)
			{
				plr.LastVelocity = value;
				plr.ExternalVelocity = value;
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


	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character"), CloneIgnore]
	public Color HeadColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.HeadColor : _pendingHeadColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.HeadColor = value;
				_pendingHeadColor = null;
			}
			else
			{
				_pendingHeadColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color TorsoColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.TorsoColor : _pendingTorsoColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.TorsoColor = value;
				_pendingTorsoColor = null;
			}
			else
			{
				_pendingTorsoColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color LeftArmColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.LeftArmColor : _pendingLeftArmColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.LeftArmColor = value;
				_pendingLeftArmColor = null;
			}
			else
			{
				_pendingLeftArmColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color RightArmColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.RightArmColor : _pendingRightArmColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.RightArmColor = value;
				_pendingRightArmColor = null;
			}
			else
			{
				_pendingRightArmColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color LeftLegColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.LeftLegColor : _pendingLeftLegColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.LeftLegColor = value;
				_pendingLeftLegColor = null;
			}
			else
			{
				_pendingLeftLegColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public Color RightLegColor
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.RightLegColor : _pendingRightLegColor ?? new Color();
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.RightLegColor = value;
				_pendingRightLegColor = null;
			}
			else
			{
				_pendingRightLegColor = value;
			}
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Apply them to Character instead"), CloneIgnore]
	public int FaceID
	{
		get => (Character is PolytorianModel polytorian) ? polytorian.FaceID : _pendingFaceID ?? 0;
		set
		{
			if (Character is PolytorianModel polytorian)
			{
				polytorian.FaceID = value;
				_pendingFaceID = null;
			}
			else
			{
				_pendingFaceID = value;
			}
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
	public float Health
	{
		get => _health;
		set
		{
			if (this is Player plr && !plr.IsReady) return;
			_health = value;
			if (_health <= 0 && !IsDead)
			{
				TriggerNPCDead();
			}
			OnPropertyChanged();
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
	public float CoyoteTime
	{
		get => _coyoteTime;
		set
		{
			_coyoteTime = value;
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
	public string DisplayName
	{
		get => _displayName;
		set
		{
			_displayName = value;
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
	public Sound? FallSound
	{
		get => _fallSound;
		set
		{
			_fallSound = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Sound? LandSound
	{
		get => _landSound;
		set
		{
			_landSound = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Sound? WalkSound
	{
		get => _walkSound;
		set
		{
			_walkSound = value;
			OnPropertyChanged();
		}
	}

	[SyncVar, ScriptProperty]
	public bool IsSitting { get; internal set; } = false;

	[SyncVar, ScriptProperty]
	public bool IsDead { get; internal set; } = false;

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

	[Editable, ScriptProperty, SyncVar]
	public CharacterModel? Character
	{
		get
		{
			if (_character != null && _character.IsDeleted)
			{
				_character = null;
			}
			return _character;
		}
		internal set => _character = value;
	}

	[SyncVar, ScriptProperty]
	public Dynamic? MoveTarget
	{
		get
		{
			if (_moveTarget != null && _moveTarget.IsDeleted)
			{
				_moveTarget = null;
			}
			return _moveTarget;
		}
		set => _moveTarget = value;
	}

	[ScriptProperty, ScriptLegacyProperty("Grounded")]
	public bool IsOnGround => CharBody3D.IsOnFloor();

	[ScriptProperty]
	public bool IsOnCeiling => CharBody3D.IsOnCeiling();

	[ScriptProperty] public float NavDestinationDistance => _navAgent == null ? Mathf.Inf : _navAgent.DistanceToTarget();

	[ScriptProperty]
	public bool NavDestinationReached { get; private set; } = false;

	[ScriptProperty] public bool NavDestinationValid => _navAgent != null && _navAgent.IsTargetReachable();

	public Vector3 CharacterVelocity = Vector3.Zero;

	[ScriptProperty]
	public PTSignal Died { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Landed { get; private set; } = new();

	[ScriptProperty]
	public PTSignal NavFinished { get; private set; } = new();

	public override Node CreateGDNode()
	{
		return new CharacterBody3D()
		{
			FloorMaxAngle = Mathf.DegToRad(80f),
			FloorSnapLength = StepHeight + 0.05f,
			FloorStopOnSlope = true
		};
	}

	public override void InitGDNode()
	{
		CharBody3D = (CharacterBody3D)GDNode;
		base.InitGDNode();
	}

	public override void Init()
	{
		base.Init();
		EnsureTouchArea();
		OverridePhysicsProcess = true;

		HashSet<BuiltInAudioAsset.BuiltInAudioPresetEnum> uniquePresets = new(_materialSounds.Values);
		foreach (var preset in uniquePresets)
		{
			var audio = New<BuiltInAudioAsset>();
			audio.AudioPreset = preset;
			_footstepAudioCache[preset] = audio;
		}

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

		RecalculateNametagOffset();

		// Force these to always be on
		SetProcess(true);
		SetPhysicsProcess(true);
	}

	public override void InitOverrides()
	{
		Anchored = false;
	}

	public override void PreDelete()
	{
		ChildAdded.Disconnect(OnChildAdded);
		ChildRemoved.Disconnect(OnChildRemoved);
		_navAgent?.NavigationFinished -= OnNavFinished;
		base.PreDelete();
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

	public override void Ready()
	{
		if (Root.IsLegacyWorld && Character == null && !PendingProps.Contains(nameof(Character)))
		{
			// Create default character on legacy world. If character is not set
			Root.Insert.InitializeDefaultNPC(this);

			if (Character is PolytorianModel polytorian)
			{
				if (_pendingHeadColor.HasValue)
				{
					polytorian.HeadColor = _pendingHeadColor.Value;
					_pendingHeadColor = null;
				}
				if (_pendingTorsoColor.HasValue)
				{
					polytorian.TorsoColor = _pendingTorsoColor.Value;
					_pendingTorsoColor = null;
				}
				if (_pendingLeftArmColor.HasValue)
				{
					polytorian.LeftArmColor = _pendingLeftArmColor.Value;
					_pendingLeftArmColor = null;
				}
				if (_pendingRightArmColor.HasValue)
				{
					polytorian.RightArmColor = _pendingRightArmColor.Value;
					_pendingRightArmColor = null;
				}
				if (_pendingLeftLegColor.HasValue)
				{
					polytorian.LeftLegColor = _pendingLeftLegColor.Value;
					_pendingLeftLegColor = null;
				}
				if (_pendingRightLegColor.HasValue)
				{
					polytorian.RightLegColor = _pendingRightLegColor.Value;
					_pendingRightLegColor = null;
				}
				if (_pendingFaceID.HasValue)
				{
					polytorian.FaceID = _pendingFaceID.Value;
					_pendingFaceID = null;
				}
			}
		}

		if (IsSitting && SittingIn != null)
		{
			InternalSit(SittingIn);
		}

		if (HoldingTool != null)
		{
			InternalAttachTool(HoldingTool);
		}

		RecalculateNametagOffset();
		base.Ready();
	}

#if CREATOR
	public override void CreatorInserted()
	{
		Root.Insert.InitializeDefaultNPC(this);
		base.CreatorInserted();
	}
#endif

	private void RecalculateNametagOffset()
	{
		if (!_nametag.IsInsideTree()) { return; }
		_nametag.Position = NametagOffset + _fixedNametagOffset;
	}

	internal void ConsumeEjectMomentum(float delta)
	{
		if (EjectMomentum.LengthSquared() <= 0.0001f) return;
		CharacterVelocity.X += EjectMomentum.X;
		CharacterVelocity.Z += EjectMomentum.Z;
		EjectMomentum = EjectMomentum.Lerp(Vector3.Zero, MathUtils.ExpDecay(delta, 2f));
	}

	protected override void OnPropertyChanged([CallerMemberName] string propertyName = "", bool syncToNet = true)
	{
		base.OnPropertyChanged(propertyName, syncToNet);
		if (syncToNet && IsSitting && !_writingSeat && propertyName is nameof(Position) or nameof(Rotation) or nameof(LocalPosition) or nameof(LocalRotation) or nameof(Quaternion) or nameof(LocalQuaternion))
		{
			Unsit(false);
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

		// Kill player if fall off the map
		if (Position.Y < Root.Environment.PartDestroyHeight)
		{
			Kill();
		}

		if (IsSitting)
		{
			if (!Root.Network.IsServer && SittingIn != null)
			{
				Velocity = Vector3.Zero;
				_writingSeat = true;
				Position = SittingIn.Position + SeatOffset.Y * Up;
				Rotation = SittingIn.SitDirectionLocked ? SittingIn.Rotation : new Vector3(SittingIn.Rotation.X, Rotation.Y, SittingIn.Rotation.Z);
				_writingSeat = false;
				Character?.PlayIdle();
			}
			if (IsSitting) return;
		}

		// Ragdoll bones fully own movement while active
		if (Character is PolytorianModel ptRagdoll && ptRagdoll.Ragdolling) return;

		if (this is Player plr)
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
			bool isOnFloor = CharBody3D.IsOnFloor();

			if (isOnFloor)
			{
				_timeSinceGrounded = 0f;
				JustJumped = false;
			}
			else
			{
				_timeSinceGrounded += (float)delta;
			}

			bool isOnCeiling = CharBody3D.IsOnCeiling();
			bool playerNPCOverride = this is Player p && !p.CanMove;

			CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;
			Vector3? walkTarget = null;
			float animSpeed = 1;

			if (MoveTarget != null)
			{
				walkTarget = MoveTarget.GetGlobalPosition();
			}

			if (_navAgent != null)
			{
				walkTarget = _navAgent.GetNextPathPosition();

				// Adjust Nav agent position in-case of unstable Y position changes
				_navAgentContainer?.GlobalPosition = _navAgentContainer.GlobalPosition with { Y = walkTarget.Value.Y };
			}

			if (walkTarget.HasValue)
			{
				Vector3 velo = GetGlobalPosition().DirectionTo(walkTarget.Value with { Y = Position.Y });
				CharacterVelocity = new(velo.X * WalkSpeed, CharacterVelocity.Y, velo.Z * WalkSpeed);
				GDNode3D.GlobalRotationDegrees = new Vector3(Rotation.X, Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(Rotation.Y), Mathf.Atan2(CharacterVelocity.X, CharacterVelocity.Z), MathUtils.ExpDecay((float)delta, BodyRotateLerp))), Rotation.Z);

				float distanceToTarget = GetGlobalPosition().DistanceTo(walkTarget.Value);

				if (distanceToTarget > 0.5f)
				{
					finalState = CharacterModel.CharacterModelStateEnum.Walking;
					animSpeed = WalkSpeed / 8;
				}
			}
			else if (this is not Player || playerNPCOverride)
			{
				CharacterVelocity = new(0, CharacterVelocity.Y, 0);
			}

			if (!isOnFloor)
			{
				finalState = CharacterModel.CharacterModelStateEnum.Jumping;
			}

			if (this is not Player || playerNPCOverride)
			{
				Character?.SetState(finalState);
				Character?.SetAnimSpeed(animSpeed);
			}

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
			if (this is not Player)
			{
				ConsumeEjectMomentum((float)delta);

				Vector3 fullVelocity = CharacterVelocity;

				CharBody3D.Velocity = new Vector3(fullVelocity.X, 0f, fullVelocity.Z);
				CharBody3D.MoveAndSlide();
				Vector3 afterHorizontal = CharBody3D.Velocity;

				float snapLength = CharBody3D.FloorSnapLength;
				if (walkTarget.HasValue && CharBody3D.IsOnFloor())
				{
					CharBody3D.FloorSnapLength = 0f;
					TryStepUp();
					CharBody3D.FloorSnapLength = snapLength;
				}

				CharBody3D.Velocity = new Vector3(0f, fullVelocity.Y, 0f);
				CharBody3D.MoveAndSlide();

				CharacterVelocity = new Vector3(afterHorizontal.X, CharBody3D.Velocity.Y, afterHorizontal.Z);
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
			}

			if (ClimbJumpCooldownRemaining > 0f)
			{
				ClimbJumpCooldownRemaining -= (float)delta;
			}

			// Stop ignoring vehicle's collision
			if (_seatExceptionReleaseTimer > 0f)
			{
				_seatExceptionReleaseTimer -= (float)delta;
				if (_seatExceptionReleaseTimer <= 0f)
				{
					foreach (CollisionObject3D body in _seatCollisionExceptions)
					{
						if (GodotObject.IsInstanceValid(body))
						{
							CharBody3D.RemoveCollisionExceptionWith(body);
						}
					}
					_seatCollisionExceptions.Clear();
				}
			}
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

	private void TriggerNPCDead()
	{
		if (IsDead) return;
		if (Root.SessionType != World.SessionTypeEnum.Client) return;

		bool wasSitting = IsSitting;
		IsDead = true;
		Anchored = true;
		OverrideCanCollide = true;
		OverrideCanCollideTo = false;
		Unsit(false);
		UpdateCollision();
		EjectMomentum = Vector3.Zero;

		Character?.Animator?.StopAnimation();
		Character?.Animator?.StopOneShotAnimation();

		if (Character is PolytorianModel ptmodel)
		{
			ptmodel.StartRagdoll(wasSitting ? Vector3.Zero : CharacterVelocity);
		}
		Died.Invoke();
	}

	[ScriptMethod]
	public bool TryStepUp()
	{
		if (CharBody3D == null || !CharBody3D.IsOnFloor()) return false;

		int slideCount = CharBody3D.GetSlideCollisionCount();

		if (slideCount <= 0)
		{
			return false;
		}

		// Use pre-slide velocity so a wall that stops XZ speed doesn't suppress stepping
		if (new Vector3(CharacterVelocity.X, 0f, CharacterVelocity.Z).LengthSquared() < 0.0001f)
		{
			return false;
		}

		var groundHit = new KinematicCollision3D();
		if (!CharBody3D.TestMove(CharBody3D.GlobalTransform, Vector3.Down * (StepHeight + 0.05f), groundHit))
		{
			return false;
		}
		float groundY = groundHit.GetPosition().Y;
		float centerToGround = CharBody3D.GlobalPosition.Y - groundY;

		for (int i = 0; i < slideCount; i++)
		{
			KinematicCollision3D col = CharBody3D.GetSlideCollision(i);

			if (GetNetObjFromProxy((Node)col.GetCollider()) is Truss)
			{
				continue;
			}

			Vector3 wallNormal = col.GetNormal() * new Vector3(1, 0, 1);
			if (wallNormal.LengthSquared() < 0.01f)
			{
				continue;
			}
			wallNormal = wallNormal.Normalized();

			// Skip walkable surfaces, only walls need stepping
			if (col.GetNormal().AngleTo(Vector3.Up) <= CharBody3D.FloorMaxAngle)
			{
				continue;
			}

			Vector3 motion = -wallNormal * 0.05f;
			Transform3D lifted = CharBody3D.GlobalTransform.Translated(Vector3.Up * StepHeight);
			var fwdHit = new KinematicCollision3D();
			Transform3D fwdTransform;

			if (!CharBody3D.TestMove(lifted, motion, fwdHit))
			{
				fwdTransform = lifted.Translated(motion);
			}
			else
			{
				// Blocked above, try sliding around a second wall (e.g. inside corners)
				Vector3 secondNormal = fwdHit.GetNormal() * new Vector3(1, 0, 1);
				if (secondNormal.IsEqualApprox(wallNormal))
				{
					continue;
				}

				motion = motion.Slide(secondNormal).Normalized() * 0.05f;

				if (CharBody3D.TestMove(lifted, motion, new KinematicCollision3D()))
				{
					continue;
				}

				fwdTransform = lifted.Translated(motion);
			}

			var downHit = new KinematicCollision3D();
			if (!CharBody3D.TestMove(fwdTransform, Vector3.Down * StepHeight, downHit))
			{
				continue;
			}

			float stepTopY = downHit.GetPosition().Y;
			float rise = stepTopY - groundY;

			if (rise <= 0.01f || rise > StepHeight)
			{
				continue;
			}

			if (downHit.GetNormal().AngleTo(Vector3.Up) > CharBody3D.FloorMaxAngle)
			{
				continue;
			}

			// Only Y changes, MoveAndSlide owns XZ.
			CharBody3D.GlobalPosition = new Vector3(CharBody3D.GlobalPosition.X, stepTopY + centerToGround, CharBody3D.GlobalPosition.Z) + (-wallNormal * 0.013f);
			CharBody3D.Velocity = CharacterVelocity;
			CharBody3D.ApplyFloorSnap();

			return true;
		}

		return false;
	}

	internal void TickPanicFall(bool isOnFloor, float delta)
	{
		bool grounded = isOnFloor || CharacterVelocity.Y >= 0f;
		if (grounded || IsClimbing || IsDead || IsSitting)
		{
			if (IsPanicFalling)
			{
				FallSound?.Stop();
				if (grounded && LandSound != null && !LandSound.Playing)
				{
					LandSound.Play();
				}
			}
			_fallTimer = 0f;
			IsPanicFalling = false;
			return;
		}

		_fallTimer += delta;
		if (_fallTimer < PanicFallDelay)
		{
			IsPanicFalling = false;
			return;
		}

		if (!IsPanicFalling)
		{
			var result = World.Current?.World3D.DirectSpaceState?.IntersectRay(new PhysicsRayQueryParameters3D
			{
				From = CharBody3D.GlobalPosition,
				To = CharBody3D.GlobalPosition + Vector3.Down * PanicFallHeight,
				CollideWithBodies = true,
				CollideWithAreas = false,
				Exclude = [CharBody3D.GetRid()]
			});
			bool nowFalling = result == null || result.Count == 0;
			if (nowFalling)
			{
				FallSound?.Loop = true;
				if (FallSound != null && !FallSound.Playing)
				{
					FallSound.Play();
				}
			}
			IsPanicFalling = nowFalling;
		}
	}

	internal void TickFootsteps(bool isOnFloor, float delta)
	{
		if (WalkSound == null || IsDead)
		{
			_distanceSinceStep = 0f;
			return;
		}

		bool climbing = IsClimbing;
		float speed = climbing ? Mathf.Abs(CharacterVelocity.Y) : new Vector2(CharacterVelocity.X, CharacterVelocity.Z).Length();
		if ((!climbing && !isOnFloor) || speed < 0.5f)
		{
			_distanceSinceStep = 0f;
			return;
		}

		_distanceSinceStep += speed * delta;
		while (_distanceSinceStep >= StepDistance)
		{
			_distanceSinceStep -= StepDistance;
			_lastStepWasLeft = !_lastStepWasLeft;
			PlayFootstep(_lastStepWasLeft, climbing ? 1.5f : 1f);
		}
	}

	internal void PlayFootstep(bool left, float pitchMultiplier = 1f)
	{
		if (WalkSound == null || CharBody3D == null || IsDead) return;
		if (!IsClimbing && !IsOnGround) return;

		var spaceState = CharBody3D.GetWorld3D().DirectSpaceState;
		var result = spaceState.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = CharBody3D.GlobalPosition + Vector3.Up * 0.3f,
			To = CharBody3D.GlobalPosition + Vector3.Down * 5f,
			CollideWithBodies = true,
			CollideWithAreas = false,
			Exclude = [CharBody3D.GetRid()]
		});

		string matName = "Plastic";
		if (result.Count > 0 && result.TryGetValue("collider", out var colliderObj))
		{
			if (colliderObj.AsGodotObject() is Node colliderNode && GetNetObjFromProxy(colliderNode) is Part part)
			{
				string name = part.Material.ToString();
				if (!string.IsNullOrEmpty(name) && _materialSounds.ContainsKey(name))
				{
					matName = name;
				}
			}
		}

		var preset = _materialSounds[matName];
		if (preset != _currentFootstepPreset)
		{
			_currentFootstepPreset = preset;
			WalkSound.Audio = _footstepAudioCache[preset];
		}

		float speed = IsClimbing ? Mathf.Abs(CharacterVelocity.Y) : new Vector2(CharacterVelocity.X, CharacterVelocity.Z).Length();
		float speedRatio = speed / _walkSpeed;
		float pitch;
		do
		{
			pitch = pitchMultiplier * speedRatio * (FootstepBasePitch + (float)GD.RandRange(-FootstepPitchVariance, FootstepPitchVariance));
		}
		while (Mathf.Abs(pitch - _lastFootstepPitch) < FootstepPitchVariance * 0.5f);

		_lastFootstepPitch = pitch;
		WalkSound.Pitch = pitch;
		WalkSound.Play();
	}

	[ScriptMethod]
	public virtual void Jump()
	{
		bool canJump = (CharBody3D.IsOnFloor() || IsClimbing || (!_coyoteUsed && _timeSinceGrounded <= CoyoteTime)) && JumpPower > 0;
		bool playJumpSound = false;
		if (canJump)
		{
			_coyoteUsed = true;
			CharacterVelocity.Y = JumpPower;
			playJumpSound = true;
			JustJumped = true;
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
	}

	[ScriptMethod]
	public void Sit(Seat seat)
	{
		Rpc(nameof(NetSit), seat.NetworkedObjectID);
	}

	[ScriptMethod]
	public void Unsit(bool addForce = true)
	{
		// Reset rotation
		_writingSeat = true;
		Rotation = new(0, Rotation.Y, 0);

		if (addForce)
		{
			Position += SeatOffset * 2;
		}
		_writingSeat = false;

		Rpc(nameof(NetJumpFromSeat));
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
		_seatExceptionReleaseTimer = 0f;
		OverrideNetworkTransform = true;
		SittingIn = seat;
		seat.Occupant = this;
		seat.InvokeSat(this);
		Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.Sitting, 1);

		// Exclude vehicles from collision
		if (seat.Parent is Physical vehicle)
		{
			if (vehicle.GetCollisionBody() is CollisionObject3D rootBody)
			{
				CharBody3D.AddCollisionExceptionWith(rootBody);
				_seatCollisionExceptions.Add(rootBody);
			}

			foreach (Instance descendant in vehicle.GetDescendants())
			{
				if (descendant is not Physical physical) continue;

				CollisionObject3D? body = physical.GetCollisionBody();
				if (body == null) continue;

				CharBody3D.AddCollisionExceptionWith(body);
				_seatCollisionExceptions.Add(body);
			}
		}
		else
		{
			CollisionObject3D? body = seat.GetCollisionBody();
			if (body != null)
			{
				CharBody3D.AddCollisionExceptionWith(body);
				_seatCollisionExceptions.Add(body);
			}
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void NetJumpFromSeat()
	{
		if (IsSitting)
		{
			Vector3 inherited = Vector3.Zero;
			if (SittingIn != null)
			{
				Physical? vehicleRoot = SittingIn.PhysicalRoot;
				inherited = vehicleRoot?.Velocity ?? SittingIn.Velocity;
			}

			// Unsit the NPC
			IsSitting = false;
			OverrideNetworkTransform = false;

			if (SittingIn != null)
			{
				SittingIn.Occupant = null;
				SittingIn.InvokeVacated(this);
				SittingIn = null;
			}

			Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.Sitting, 0);

			// Extend vehicle exception slightly to avoid
			_seatExceptionReleaseTimer = SeatExceptionReleaseDelay;

			if (!IsDead)
			{
				EjectMomentum = (inherited with { Y = 0 }) * EjectMomentumScale;

				// Don't restore collision if death already claimed the override (ragdoll)
				OverrideCanCollide = false;
				UpdateCollision();
			}
		}
	}

	[ScriptMethod]
	public void EquipTool(Tool tool)
	{
		if (IsDead) return;
		// Check if tool is already held
		if (HoldingTool != null)
		{
			if (this is Player plr)
			{
				plr.UnequipTool();
			}
			else
			{
				DropTool();
			}
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

		if (Character != null)
		{
			Dynamic attachment = Character.GetAttachment(CharacterModel.CharacterAttachmentEnum.HandRight);
			attachment.GDNode.AddChild(_toolRemoteTransform, @internal: Node.InternalMode.Back);
		}

		// stick and stones
		// this is needed because GetPath doesn't update when it entered tree
		await Globals.Singleton.WaitFrame();
		_toolRemoteTransform.Position = new Vector3(0, 0, 0);
		_toolRemoteTransform.RotationDegrees = new Vector3(0, -90, -90);
		_toolRemoteTransform.UpdateScale = false;
		_toolRemoteTransform.RemotePath = _toolRemoteTransform.GetPathTo(tool.GDNode);
	}

	internal void InternalDetachTool()
	{
		if (_toolRemoteTransform != null && Node.IsInstanceValid(_toolRemoteTransform))
		{
			_toolRemoteTransform?.QueueFree();
		}

		Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.ToolHoldRight, 0);
	}

	[ScriptMethod, ScriptLegacyMethod("DropTools")]
	public void DropTool()
	{
		if (HoldingTool != null)
		{
			Tool tool = HoldingTool;
			if (this is Player plr)
			{
				plr.UnequipTool();
			}
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
			InternalDetachTool();
			tool.InvokeDropped();
		}
	}

	[ScriptMethod]
	public void LoadAppearance(int userID)
	{
		if (Character is PolytorianModel ptm)
		{
			ptm.LoadAppearance(userID, Root.PlayerDefaults.LoadAppearanceTools);
		}
	}

	[ScriptMethod]
	public void ClearAppearance()
	{
		if (Character is PolytorianModel ptm)
		{
			ptm.ClearAppearance();
		}
	}

	[ScriptMethod]
	public void SetNavDestination(Vector3 pos)
	{
		MoveTarget = null;
		if (_navAgent == null)
		{
			_navAgentContainer = new();
			_navAgent = new()
			{
				PathDesiredDistance = NavigationDistance,
				TargetDesiredDistance = 0.5f,
				PathHeightOffset = -(CalculateBounds().Size.Y / 2),
				PathMaxDistance = 3f
			};

			_navAgentContainer.AddChild(_navAgent);
			GDNode3D.AddChild(_navAgentContainer);
			if (Globals.IsInGDEditor)
			{
				_navAgent.DebugEnabled = true;
			}

			_navAgent.NavigationFinished += OnNavFinished;
			NavDestinationReached = false;
		}
		_navAgent.TargetPosition = pos;
	}

	private void OnNavFinished()
	{
		_navAgentContainer?.QueueFree();
		_navAgent = null;
		NavDestinationReached = true;
		NavFinished.Invoke();
	}

	[ScriptMethod]
	public void Respawn()
	{
		Health = MaxHealth;
		Anchored = false;
		IsDead = false;
		EjectMomentum = Vector3.Zero;

		Character?.Animator?.StopAnimation();
		Character?.Animator?.StopOneShotAnimation();

		if (Character is PolytorianModel ptmodel)
		{
			ptmodel.StopRagdoll();
		}
		CharacterVelocity = Vector3.Zero;

		if (this is Player plr)
		{
			plr.LastVelocity = Vector3.Zero;
			plr.ExternalVelocity = Vector3.Zero;
		}

		OverrideCanCollide = false;
		UpdateCollision();
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
}
