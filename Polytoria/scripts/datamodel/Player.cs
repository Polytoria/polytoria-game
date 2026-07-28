// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI.Chat;
#if CREATOR
#endif
using Polytoria.Datamodel.Services;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Networking;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using Polytoria.Providers.PlayerMovement;

namespace Polytoria.Datamodel;

[ExplorerExclude]
public sealed partial class Player : NPC
{
	private const double MaxAFKTime = 60 * 15;
	private const float CameraHeight = 2f;
	public const string CreatorHeadScene = "res://scenes/creator/livecollab/head.tscn";
	public const string BubbleChatScene = "res://scenes/client/spatial/chat/bubble_chat.tscn";
	public const string BadgeImageDirPath = "res://assets/textures/client/ui/playerlist/badges/";
	private static readonly Dictionary<string, string> _badgePathCache = [];
	private bool _isReady = false;
	private static float ClimbPushSpeed = 25.0f;
	private static float ClimbJumpCooldown = 0.6f;
	internal bool JustFinishedClimbing = false;
	internal bool IsMoving = false;
	internal IPlayerMovement? PlayerMovement;
	internal Vector3 LastVelocity;
	internal Vector3 ExternalVelocity;

	private int _userID;
	private bool _allowAnimationWhileMoving = false;
	private PlayerRotationModeEnum _rotationMode = PlayerRotationModeEnum.Automatic;
	private Team? _team;
	private Color _chatColorBeforeTeam;
	private float _stamina = 0;

	private float _respawnTime;
	private float _sprintSpeed;
	private bool _useStamina;
	private float _maxStamina;
	private float _staminaBurn;
	private float _staminaRegen;
	private bool _canMove;
	private bool _canRespawn;
	private float _airControl;
	private float _airFriction;
	private float _groundControl;
	private float _groundFriction;
	private bool _useHeadTurning;
	private bool _useFootplanting;
	private bool _useBuiltInSounds;
	private bool _useFirstPersonViewmodel;
	private bool _autoLoadAppearance;
	private bool _keepInventory;
	private bool _useBubbleChat;
	private PlayerMovementModeEnum _movementMode;
	private PlayerCollisionShapeEnum _collisionShape;

	internal bool SprintOverride = false;
	private float _pingStartTime = 0;
	internal bool SprintHoldAgain = false;

	private double _afkTimer;

	internal bool teleporting = false;

	private BubbleChat _bubbleChat = null!;
	private RemoteTransform3D _remoteCamAttach = null!;
	internal Dynamic CamAttach = null!;
	private Physical? _mouseHoveringOn;

	private Vector3 DefaultSpawnLocation = new(0, 5, 0);
	internal event Action<APIUserInfo>? UserInfoReady;

#if CREATOR
	private bool _spawnedAtCreatorPos = false;
#endif

	// internal peer ID
	[SyncVar]
	public int PeerID { get; set; }

	[SyncVar]
	public bool CanChat { get; set; } = false;

	[SyncVar]
	public bool IsAgeRestricted { get; set; } = false;

	internal APIUserInfo? UserInfo { get; private set; }

	[ScriptProperty]
	public PTSignal<string> Chatted { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Stat, object?> StatChanged { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Team?> TeamChanged { get; private set; } = new();

	[ScriptProperty]
	public PTSignal Respawned { get; private set; } = new();

	[SyncVar, ScriptProperty]
	public int UserID
	{
		get => _userID;
		internal set
		{
			_userID = value;
			if (_userID != 0)
			{
				FetchUserInfo();
			}
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
	public PlayerRotationModeEnum RotationMode
	{
		get => _rotationMode;
		set
		{
			_rotationMode = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Team? Team
	{
		get => _team;
		set
		{
			var old = _team;
			_team = value;
			if (_team != old)
			{
				TeamChanged.Invoke(_team);
				Root.Teams.DispatchTeamUpdate();
				if (value != null)
				{
					_chatColorBeforeTeam = ChatColor;
					ChatColor = value.Color;
				}
				else
					ChatColor = _chatColorBeforeTeam;
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float RespawnTime
	{
		get => _respawnTime;
		set
		{
			_respawnTime = value;
			OnPropertyChanged();
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
	public bool CanMove
	{
		get => _canMove;
		set
		{
			_canMove = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool CanRespawn
	{
		get => _canRespawn;
		set
		{
			_canRespawn = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float AirControl
	{
		get => _airControl;
		set
		{
			_airControl = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float AirFriction
	{
		get => _airFriction;
		set
		{
			_airFriction = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float GroundControl
	{
		get => _groundControl;
		set
		{
			_groundControl = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float GroundFriction
	{
		get => _groundFriction;
		set
		{
			_groundFriction = value;
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

	[Editable, ScriptProperty]
	public bool UseFootplanting
	{
		get => _useFootplanting;
		set
		{
			_useFootplanting = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseBuiltInSounds
	{
		get => _useBuiltInSounds;
		set
		{
			_useBuiltInSounds = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseFirstPersonViewmodel
	{
		get => _useFirstPersonViewmodel;
		set
		{
			_useFirstPersonViewmodel = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AutoLoadAppearance
	{
		get => _autoLoadAppearance;
		set
		{
			_autoLoadAppearance = value;
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

	[Editable, ScriptProperty]
	public PlayerCollisionShapeEnum CollisionShape
	{
		get => _collisionShape;
		set
		{
			_collisionShape = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public PlayerMovementModeEnum MovementMode
	{
		get => _movementMode;
		set
		{
			_movementMode = value;

			PlayerMovement = _movementMode switch
			{
				PlayerMovementModeEnum.Default => new DefaultMovement() { Root = Root, Target = this },
				_ => null,
			};

			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public int NetworkPing { get; private set; }

	[ScriptProperty, SyncVar]
	public bool IsAdmin { get; internal set; }

	[ScriptProperty, SyncVar]
	public bool IsCreator { get; internal set; }

	[ScriptProperty, SyncVar]
	public string UserRoleClass { get; internal set; } = "";

	[ScriptProperty, SyncVar]
	public Color ChatColor { get; set; } = new(1, 1, 1);

	private static readonly Color[] ChatColorPalette =
		[
			Color.FromHtml("#4e9aa8"),
			Color.FromHtml("#00a86b"),
			Color.FromHtml("#4b3f69"),
			Color.FromHtml("#d8ad39"),
			Color.FromHtml("#d6c69a"),
			Color.FromHtml("#26A69A"),
			Color.FromHtml("#7CB342"),
			Color.FromHtml("#5C6BC0"),
			Color.FromHtml("#FB7EFD"),
			Color.FromHtml("#54A0FF"),
			Color.FromHtml("#5F27CD"),
			Color.FromHtml("#01A3A4"),
			Color.FromHtml("#F368E0"),
			Color.FromHtml("#FF9F43"),
			Color.FromHtml("#1DD1A1"),
			Color.FromHtml("#48DBFB"),
			Color.FromHtml("#AB47BC"),
			Color.FromHtml("#42A5F5"),
			Color.FromHtml("#66BB6A"),
			Color.FromHtml("#FFA726"),
			Color.FromHtml("#8D6E63"),
			Color.FromHtml("#78909C"),
			Color.FromHtml("#D4E157"),
			Color.FromHtml("#B39DDB"),
		];

	public static Color ChatColorFromUserID(int userID)
	{
		return ChatColorPalette[userID % ChatColorPalette.Length];
	}

	public static string GetBadgeIconPath(Player player)
	{
		string badgeName = player.IsCreator ? "creator"
			: !string.IsNullOrEmpty(player.UserRoleClass) ? player.UserRoleClass
			: player.IsAdmin ? "admin"
			: "";

		if (string.IsNullOrEmpty(badgeName))
			return "";

		if (_badgePathCache.TryGetValue(badgeName, out string? cached))
			return cached;

		string path = BadgeImageDirPath.PathJoin(badgeName + ".png");
		string result = ResourceLoader.Exists(path) ? path : "";
		_badgePathCache[badgeName] = result;
		return result;
	}

	[ScriptProperty, Attributes.Obsolete("Use Input.IsInputFocused instead")]
	public bool IsInputFocused => Root.Input.IsInputFocused;

	[ScriptProperty]
	public bool IsLocal { get; private set; }

	[SyncVar(AllowAuthorWrite = true), ScriptProperty]
	public bool IsClimbing { get; internal set; }

	[SyncVar(AllowAuthorWrite = true), ScriptProperty]
	public Truss? ClimbingTruss { get; internal set; }

	[SyncVar(ServerOnly = true)]
	public bool IsReady
	{
		get => _isReady;
		set
		{
			bool oldVal = _isReady;
			_isReady = value;
			GD.PushWarning($"{Name} update is ready {oldVal} -> {_isReady}");
			UpdatePlrReady();
			if (value != oldVal && value)
			{
				OnPlayerReady();
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar]
	public NetworkService.ClientPlatformEnum UserPlatform { get; internal set; }

	[ScriptProperty]
	public Inventory Inventory => FindChild<Inventory>("Inventory")!;

	[Attributes.Obsolete("Use Inventory instead"), ScriptProperty]
	public Inventory Backpack => Inventory;

	// Emotes visible in emote wheel
	public static readonly string[] EmoteWheelList =
	[
		"wave",
		"dance",
		"dance2",
		"helicopter",
		"sit",
		"agree",
		"disagree",
	];

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

	public override void InitGDNode()
	{
		base.InitGDNode();
		CollisionLayers = 2;
		CollisionMask = 3;
	}

	public override void Init()
	{
		base.Init();

		Root.Input.GodotInputEvent += OnInput;

		if (Root.SessionType == World.SessionTypeEnum.Client && Root.Network.IsServer)
		{
			Inventory inventory = Globals.LoadInstance<Inventory>(Root);
			inventory.NameOverride = "Inventory";
			inventory.NetworkParent = this;
		}

		Died.Connect(OnPlayerDied);
		Root.Players.PropertyChanged.Connect(OnPlayersPropertyChanged);

		_bubbleChat = Globals.CreateInstanceFromScene<BubbleChat>(BubbleChatScene);
		_bubbleChat.TargetPlayer = this;
		_bubbleChat.Visible = _useBubbleChat;
		GDNode.AddChild(_bubbleChat, @internal: Node.InternalMode.Back);
		excludedBoundNodes.Add(_bubbleChat);
	}

	public override void PreDelete()
	{
		Root.Input.GodotInputEvent -= OnInput;
		Died.Disconnect(OnPlayerDied);
		PlayerMovement = null!;
		base.PreDelete();
	}

	private void OnPlayerTouched(Physical obj)
	{
		obj.InvokeTouched(this);
	}

	private void OnPlayerTouchEnded(Physical obj)
	{
		obj.InvokeTouchEnded(this);
	}

	public override void Ready()
	{
		base.Ready();
		OnPlayerReady();
	}

	private void UpdatePlrReady()
	{
		SetCollisionDisabled(!_isReady);
		GDNode3D?.Visible = _isReady;
	}

	private void SetCamRemoteAttachEnabled(bool enabled)
	{
		_remoteCamAttach.UpdatePosition = enabled;
		_remoteCamAttach.UpdateRotation = enabled;
		if (enabled == false)
		{
			CamAttach.LocalPosition = new Vector3(0, CameraHeight, 0);
		}
	}

	private void OnPlayersPropertyChanged(string propName)
	{
		if (propName == "PlayerCollisionEnabled")
		{
			UpdatePlayerCollision();
		}
	}

	private async void FetchUserInfo()
	{
		UserInfo = await PolyAPI.GetUserFromID(UserID);
		if (UserInfo.HasValue)
		{
			UserInfoReady?.Invoke(UserInfo.Value);
		}
	}

	private void UpdatePlayerCollision()
	{
		if (Root.Players.PlayerCollisionEnabled)
		{
			SetCollisionMask(2, true);
		}
		else
		{
			SetCollisionMask(2, false);
		}
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (!Root.Network.IsServer)
		{
			UpdateCamera(delta);
		}
		if (!IsLocal)
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

		if (!IsLocal || !IsReady)
		{
			return;
		}
	}

	private void UpdateCamera(double delta)
	{
		if (Root.Environment.CurrentCamera?.Mode != Camera.CameraModeEnum.Scripted)
		{
			Root.Environment.CurrentCamera?.CameraProcess(delta);
		}
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

	private void AfkTick(double delta)
	{
		// Disable AFK kick if local test
		if (Root.IsLocalTest) return;

		if (_afkTimer > MaxAFKTime)
		{
			Root.Network.DisconnectSelf("You have been kicked from the server for being inactive for too long.", NetworkService.DisconnectionCodeEnum.AFK);
			return;
		}

		_afkTimer += delta;

		if (Input.IsAnythingPressed())
		{
			_afkTimer = 0;
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

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);

		if (Root.SessionType != World.SessionTypeEnum.Client || !IsLocal || !IsReady) { return; }

		if (Character is PolytorianModel pt && pt.Ragdolling)
		{
			// ragdoll camera update
			UpdateCamera(delta);
			return;
		}

		Environment.RayResult? ray = Root.Environment.CurrentCamera?.ScreenPointToRay(Root.Input.MousePosition);
		if (ray.HasValue && ray.Value.Instance is Physical p)
		{
			if (_mouseHoveringOn != null && _mouseHoveringOn != p)
			{
				_mouseHoveringOn.MouseExit.Invoke();
			}
			_mouseHoveringOn = p;
			_mouseHoveringOn.MouseEnter.Invoke();
		}

		if (FootFwdRaycast.IsColliding())
		{
			Node collider = (Node)FootFwdRaycast.GetCollider();
			Vector3 hitNormal = FootFwdRaycast.GetCollisionNormal(); //Prevents climbing from triggering while grounded on top of a truss
			bool facingClimbableSurface = hitNormal.AngleTo(Vector3.Up) > Mathf.DegToRad(45f);
			if (collider != null && facingClimbableSurface && GetNetObjFromProxy(collider) is Truss truss)
			{
				if (!IsClimbing)
				{
					if (ClimbJumpCooldownRemaining <= 0f)
					{
						ClimbingTruss = truss;
						IsClimbing = true;
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

		if (Anchored)
		{
			// just in case it's anchored cuz ragdoll
			if (Character is PolytorianModel pt2 && pt2.Ragdolling == false)
			{
				UpdateCamera(delta);
			}
			AfkTick(delta);
			return;
		}

		Camera? cam = Root.Environment.CurrentCamera;

		// Apply camera modifier if enabled
		if (UseHeadTurning && cam != null && cam.Mode == Camera.CameraModeEnum.Follow && cam.Target == CamAttach)
		{
			Character?.ApplyCameraModifier(cam);
		}

		if (IsSitting)
		{
			// Add stamina while sitting
			AddStaminaTick(delta);
			UpdateCamera(delta);
			return;
		}

		if (PlayerMovement != null)
		{
			var snapshot = PlayerMovement.SampleInput(delta);
			PlayerMovement.ProcessInput(snapshot);
		}
		else
		{
			IsMoving = Velocity.Length() > 0.01f;
		}

		// Stop animation on move
		if (IsMoving && !AllowAnimationWhileMoving)
		{
			Character?.Animator?.StopAnimation();
		}

		AfkTick(delta);

		ApplyPushForce();
	}

	internal void EndClimb()
	{
		if (!IsClimbing) { return; }
		IsClimbing = false;
		JustFinishedClimbing = true;
		ClimbingTruss = null;
	}

	private void SendPing()
	{
		_pingStartTime = Time.GetTicksMsec();
		RpcId(1, nameof(NetPingRecv));
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetPingRecv()
	{
		RpcId(PeerID, nameof(NetPong));
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void NetPong()
	{
		NetworkPing = (int)Math.Round(Time.GetTicksMsec() - _pingStartTime);
		await Globals.Singleton.WaitAsync(1);
		SendPing();
	}

	public void OnInput(InputEvent @event)
	{
		if (Root.SessionType != World.SessionTypeEnum.Client) { return; }
		if (!IsLocal || !Root.Input.IsGameFocused) { return; }

		if (@event.IsActionPressed("activate"))
		{
			Environment.RayResult? ray = Root.Environment.CurrentCamera?.ScreenPointToRay(Root.Input.MousePosition);
			if (ray.HasValue && ray.Value.Instance is Physical p)
			{
				p.InvokeClicked(this);
			}
		}

		if (@event.IsActionPressed("toggle_freecam") && (IsAdmin || IsCreator))
		{
			if (Root.Environment.CurrentCamera?.Mode == Camera.CameraModeEnum.Free)
			{
				Root.Environment.CurrentCamera.Mode = Camera.CameraModeEnum.Follow;
				CanMove = true;
			}
			else
			{
				Root.Environment.CurrentCamera?.Mode = Camera.CameraModeEnum.Free;
				CanMove = false;
			}
		}

		if (IsDead) { return; }

		if (@event.IsActionPressed("jump"))
		{
			// Ignore jump command if is custom
			if (MovementMode == PlayerMovementModeEnum.Scripted) return;
			if (!CanMove) return;
			Jump();
		}
		else if (@event.IsActionPressed("toggle_sprint"))
		{
			SprintOverride = !SprintOverride;
		}
		else if (@event.IsActionPressed("drop_tool"))
		{
			DropTool();
		}
	}

	private async void OnPlayerDied()
	{
		if (IsLocal)
		{
			UnequipTool();
		}
		Velocity = Vector3.Zero;
		if (!Root.Network.IsServer) return; // Respawn on server only

		// Respawn on client
		await Globals.Singleton.WaitAsync(RespawnTime);
		Respawn();
	}

	// Emit when network has received LocalPlayer, This can also be used to initialize localplayer
	internal void OnNetReady()
	{
		IsLocal = true;
		SendPing();

		CamAttach = Globals.LoadInstance<Dynamic>(Root);
		CamAttach.Name = "CameraAttachment";
		CamAttach.Parent = this;
		CamAttach.AutoUpdateNetTransform = false;

		_remoteCamAttach = new();
		Character?.GetAttachment(CharacterModel.CharacterAttachmentEnum.Head).GDNode.AddChild(_remoteCamAttach, @internal: Node.InternalMode.Back);
		_remoteCamAttach.RemotePath = _remoteCamAttach.GetPathTo(CamAttach.GDNode3D);

		SetCamRemoteAttachEnabled(false);

		Camera? cam = Root.Environment.CurrentCamera;
		if (cam == null) return;
		cam.Target = CamAttach;
		cam.UpdateCameraSelf = false;
		cam.FirstPersonEntered.Connect(OnFirstPersonEntered);
		cam.FirstPersonExited.Connect(OnFirstPersonExited);

		// Listen to touch events
		Touched.Connect(OnPlayerTouched);
		TouchEnded.Connect(OnPlayerTouchEnded);
		EnableCanTouch();

		// Disable auto update, this will be updated manually
		AutoUpdateNetTransform = false;

		if (Character is PolytorianModel ptc)
		{
			ptc.RagdollStarted.Connect(OnRagdollStarted);
			ptc.RagdollStopped.Connect(OnRagdollStopped);
		}
	}

	// Emit when this player is ready, fired for everyone
	private void OnPlayerReady()
	{
		SetNetworkAuthority(PeerID);
		UpdatePlayerCollision();
		UpdatePlrReady();
	}

	private void OnRagdollStarted()
	{
		SetCamRemoteAttachEnabled(true);
	}

	private void OnRagdollStopped()
	{
		SetCamRemoteAttachEnabled(false);
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

		Character?.Animator?.StopAnimation();
		Character?.Animator?.StopOneShotAnimation();

		if (isOneShot)
		{
			Character?.Animator?.PlayOneShotAnimation("emote_" + emoteName);
		}
		else
		{
			Character?.Animator?.PlayAnimation("emote_" + emoteName);
		}
	}

	internal void InvokeChatted(string msg)
	{
		Chatted.Invoke(msg);
	}

	public override void Jump()
	{
		bool wasClimbing = IsClimbing;
		if (wasClimbing && ClimbJumpCooldownRemaining > 0f) return;

		Vector3? pushDir = wasClimbing && ClimbingTruss != null ? (GetGlobalPosition() - ClimbingTruss.GetGlobalPosition()).Normalized() with { Y = 0 } : null;

		base.Jump();

		if (wasClimbing)
		{
			EndClimb();
			ClimbJumpCooldownRemaining = ClimbJumpCooldown;

			if (pushDir.HasValue && pushDir.Value.LengthSquared() > 0.0001f)
			{
				Vector3 dir = pushDir.Value.Normalized();
				ExternalVelocity = new Vector3(dir.X * ClimbPushSpeed, ExternalVelocity.Y, dir.Z * ClimbPushSpeed);
			}
		}
	}

	private void OnFirstPersonEntered()
	{
		if (Character == null) return;
		_bubbleChat.Visible = false;
		(Character as PolytorianModel)?.SetViewmodelActive(true);
	}

	private void OnFirstPersonExited()
	{
		if (Character == null) return;
		_bubbleChat.Visible = true;
		(Character as PolytorianModel)?.SetViewmodelActive(false);
	}

	public void WrapToSpawnPoint()
	{
		if (Root.Environment.SpawnPoints.Count > 0)
		{
			Entity spawnpoint = ArrayUtils.GetRandom(Root.Environment.SpawnPoints);
			Position = spawnpoint.Position + new Vector3(0, spawnpoint.Size.Y + 2.0f, 0);
			Rotation = new(0, spawnpoint.Rotation.Y, 0);
		}
		else
		{
			Position = DefaultSpawnLocation;
			Rotation = new(0, 0, 0);
		}

		// Spawn at custom position
#if CREATOR
		if (Root.Entry != null && Root.Entry.DebugSpawnPos != null)
		{
			if (!_spawnedAtCreatorPos)
			{
				_spawnedAtCreatorPos = true;
				Position = Root.Entry.DebugSpawnPos.Value;
				Rotation = Vector3.Zero;
			}
		}
#endif
		SendNetTransformReliable();
	}

	[ScriptMethod]
	public void Kick(string reason)
	{
		if (Root.Network.IsServer)
		{
			// Kick by server
			Root.Network.DisconnectPeer((int)PeerID, reason, NetworkService.DisconnectionCodeEnum.Kicked);
		}
		else if (Root.Network.LocalPeerID == PeerID)
		{
			// Kick themselves
			Root.Network.DisconnectSelf(reason, NetworkService.DisconnectionCodeEnum.Kicked);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PurchasesService.OwnsItem instead")]
	public void OwnsItem(int assetId, PTCallback callback)
	{
		Root.Purchases.OwnsItemAsync(this, assetId).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				bool owns = tsk.Result;
				callback.Invoke(false, owns);
			}
			else
			{
				callback.Invoke(true, false);
			}
		});
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

		Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.ToolHoldRight, 0);
		tool.Parent = Inventory;
		HoldingTool = null;
		tool.InvokeUnequipped();
		InternalDetachTool();
	}

	[ScriptMethod]
	public new void Respawn()
	{
		InternalSpawn();
	}

	private void InternalSpawn()
	{
		// Clear & Re-copy inventory
		CopyInventory();

		// Apply playerdefaults
		RespawnTime = Root.PlayerDefaults.RespawnTime;
		MaxHealth = Root.PlayerDefaults.MaxHealth;
		JumpPower = Root.PlayerDefaults.JumpPower;
		CoyoteTime = Root.PlayerDefaults.CoyoteTime;
		WalkSpeed = Root.PlayerDefaults.WalkSpeed;
		SprintSpeed = Root.PlayerDefaults.SprintSpeed;
		UseStamina = Root.PlayerDefaults.UseStamina;
		MaxStamina = Root.PlayerDefaults.MaxStamina;
		StaminaBurn = Root.PlayerDefaults.StaminaBurn;
		StaminaRegen = Root.PlayerDefaults.StaminaRegen;
		CanMove = Root.PlayerDefaults.CanMove;
		CanRespawn = Root.PlayerDefaults.CanRespawn;
		AirControl = Root.PlayerDefaults.AirControl;
		AirFriction = Root.PlayerDefaults.AirFriction;
		GroundControl = Root.PlayerDefaults.GroundControl;
		GroundFriction = Root.PlayerDefaults.GroundFriction;
		UseHeadTurning = Root.PlayerDefaults.UseHeadTurning;
		UseFootplanting = Root.PlayerDefaults.UseFootplanting;
		UseBuiltInSounds = Root.PlayerDefaults.UseBuiltInSounds;
		UseFirstPersonViewmodel = Root.PlayerDefaults.UseFirstPersonViewmodel;
		AutoLoadAppearance = Root.PlayerDefaults.AutoLoadAppearance;
		KeepInventory = Root.PlayerDefaults.KeepInventory;
		UseBubbleChat = Root.PlayerDefaults.UseBubbleChat;
		CollisionShape = Root.PlayerDefaults.CollisionShape;
		MovementMode = Root.PlayerDefaults.MovementMode;

		Character?.Animator?.StopAnimation();
		Character?.Animator?.StopOneShotAnimation();

		if (Character is PolytorianModel ptmodel)
		{
			ptmodel.StopRagdoll();
		}
		Velocity = Vector3.Zero;

		ResetAppearance();
		WrapToSpawnPoint();

		Health = MaxHealth;
		Stamina = MaxStamina;
		Anchored = false;

		Rpc(nameof(NetRespawned));
	}

	private void CopyInventory()
	{
		// Only allow this operation in server
		if (!Root.Network.IsServer) return;

		if (!KeepInventory)
		{
			foreach (Instance item in Inventory.GetChildren())
			{
				item.Delete();
			}
		}

		if (Root.PlayerDefaults.Inventory != null)
		{
			foreach (Instance item in Root.PlayerDefaults.Inventory.GetChildren())
			{
				NetworkedObject a = item.Clone();
				if (a is Instance i)
				{
					i.Parent = Inventory;
				}
			}
		}
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetRespawned()
	{
		Respawned?.Invoke();

		OverrideCanCollide = false;
		UpdateCollision();
		IsDead = false;
	}

	[ScriptMethod]
	public void ResetAppearance()
	{
		ClearAppearance();
		if (AutoLoadAppearance)
		{
			if (Root.Entry != null && Root.Entry.IsSoloTest)
			{
				LoadAppearance(1144);
			}
			else
			{
				LoadAppearance(UserID);
			}
		}
	}

	internal override bool TransformNetworkCheck(TransformPayloadDto newTransform)
	{
		// TODO: Make sanity checks here
		return true;
	}

	internal void AdminKick()
	{
		RpcId(1, nameof(NetAdminKick));
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetAdminKick()
	{
		var sender = Root.Players.GetPlayerFromPeerID(RemoteSenderId);

		if (sender == null) return; // Sender doesn't exist ?

		// If is creator or is admin
		if (sender.IsCreator || sender.IsAdmin)
		{
			Kick("You have been kicked by game administrator.");
		}
	}

	[ScriptEnum]
	public enum PlayerCollisionShapeEnum
	{
		Capsule,
		Box
	}

	[ScriptEnum]
	public enum PlayerMovementModeEnum
	{
		Default,
		Scripted
	}

	[ScriptEnum]
	public enum PlayerRotationModeEnum
	{
		Automatic, // Default value (works how it did before), automatically switches between rotating to movement or facing camera when Ctrl Locked or in First Person
		CameraLocked,
		Movement,
		MovementCtrlLockOnly // separate version that still locks in First Person, will only rotate to movement when Ctrl Locked
	}
}
