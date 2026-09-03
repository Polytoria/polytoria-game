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
	public const string CreatorHeadScene = "res://scenes/creator/livecollab/head.tscn";
	public const string BadgeImageDirPath = "res://assets/textures/client/ui/playerlist/badges/";
	private static readonly Dictionary<string, string> _badgePathCache = [];
	private bool _isReady = false;
	internal bool IsMoving = false;
	internal IPlayerMovement? PlayerMovement;

	private float _respawnTime = 5.0f;
	private int _userID;
	private bool _autoLoadAppearance = true;
	private PlayerMovementModeEnum _movementMode = PlayerMovementModeEnum.Default;
	private PlayerRotationModeEnum _rotationMode = PlayerRotationModeEnum.Automatic;
	private Team? _team;
	private Color _chatColorBeforeTeam;

	internal bool SprintOverride = false;
	private float _pingStartTime = 0;
	internal bool SprintHoldAgain = false;

	private double _afkTimer;

	internal bool teleporting = false;

	private Physical? _mouseHoveringOn;

	public Vector3 DefaultSpawnLocation = new(0, 5, 0);
	internal event Action<APIUserInfo>? UserInfoReady;

#if CREATOR
	internal bool _spawnedAtCreatorPos = false;
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

	[ScriptProperty]
	public PTSignal<CharacterModel?> CharacterChanged { get; private set; } = new();

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

	[Attributes.Obsolete("Use Character.Inventory instead"), ScriptProperty]
	public Inventory? Inventory => Character?.Inventory;

	[Attributes.Obsolete("Use Character.Inventory instead"), ScriptProperty]
	public Inventory? Backpack => Character?.Inventory;

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

	public override void Init()
	{
		// Force these to always be on
		SetProcess(true);
		SetPhysicsProcess(true);
		base.Init();

		Root.Input.GodotInputEvent += OnInput;

		Root.Players.PropertyChanged.Connect(OnPlayersPropertyChanged);
	}

	public override void PreDelete()
	{
		Root.Input.GodotInputEvent -= OnInput;
		PlayerMovement = null!;
		base.PreDelete();
	}

	public override void Ready()
	{
		base.Ready();
		OnPlayerReady();
	}

	private void UpdatePlrReady()
	{
		Character?.SetCollisionDisabled(!_isReady);
		Character?.GDNode3D?.Visible = _isReady;
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
		Character?.SetCollisionMask(2, Root.Players.PlayerCollisionEnabled);
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (!IsLocal || !IsReady)
		{
			return;
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

	public override void PhysicsProcess(double delta)
	{
		if (Root.SessionType != World.SessionTypeEnum.Client || !IsLocal || !IsReady || Character == null) { return; }

		// not ideal TODO: have this only run when needed
		Character.SetPhysicsProcess(true);
		Character.NetworkAuthority = Root.Network.LocalPeerID;

		if (Character is PolytorianModel pt && pt.Ragdolling)
		{
			return;
		}

		Environment.RayResult? ray = Root.Environment.CurrentCamera?.ScreenPointToRay(Root.Input.MousePosition);
		if (ray.HasValue && ray.Value.Instance is Physical p)
		{
			if (_mouseHoveringOn != p)
			{
				if (_mouseHoveringOn != null)
				{
					_mouseHoveringOn.MouseExit.Invoke();
				}
				_mouseHoveringOn = p;
				_mouseHoveringOn.MouseEnter.Invoke();
			}
		}
		else if (_mouseHoveringOn != null)
		{
			_mouseHoveringOn.MouseExit.Invoke();
			_mouseHoveringOn = null;
		}

		if (Character.Anchored)
		{
			// just in case it's anchored cuz ragdoll
			if (Character is PolytorianModel pt2 && pt2.Ragdolling == false)
			{
			}
			AfkTick(delta);
			return;
		}

		Camera? cam = Root.Environment.CurrentCamera;

		// Apply camera modifier if enabled
		if (Character.UseHeadTurning && cam != null && cam.Mode == Camera.CameraModeEnum.Follow && cam.Target == Character)
		{
			Character.ApplyCameraModifier(cam);
		}

		if (PlayerMovement != null)
		{
			var snapshot = PlayerMovement.SampleInput(delta);
			PlayerMovement.ProcessInput(snapshot);
		}
		else
		{
			IsMoving = Character.Velocity.Length() > 0.01f;
		}

		// Stop animation on move
		if (IsMoving && !Character.AllowAnimationWhileMoving)
		{
			Character.Animator?.StopAnimation();
		}

		AfkTick(delta);

		Character.ApplyPushForce();
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
				Character?.CanMove = true;
			}
			else
			{
				Root.Environment.CurrentCamera?.Mode = Camera.CameraModeEnum.Free;
				Character?.CanMove = false;
			}
		}

		if (Character == null) return;

		if (Character.IsDead) { return; }

		if (@event.IsActionPressed("jump"))
		{
			// Ignore jump command if is custom
			if (MovementMode == PlayerMovementModeEnum.Scripted) return;
			if (!Character.CanMove) return;
			Character._canJumpWhileClimbing = true;
			Character.Jump();
		}
		else if (@event.IsActionPressed("toggle_sprint"))
		{
			SprintOverride = !SprintOverride;
		}
		else if (@event.IsActionPressed("drop_tool"))
		{
			Character?.DropTool();
		}
	}

	internal async void OnPlayerDied()
	{
		if (!Root.Network.IsServer) return; // Respawn on server only

		if (!Root.PlayerDefaults.AutomaticSpawn) return;
		// Respawn on client
		await Globals.Singleton.WaitAsync(RespawnTime);
		Respawn();
	}

	// Emit when network has received LocalPlayer, This can also be used to initialize localplayer
	internal void OnNetReady()
	{
		IsLocal = true;
		SendPing();

		Camera? cam = Root.Environment.CurrentCamera;
		if (cam == null) return;
		cam.FirstPersonEntered.Connect(OnFirstPersonEntered);
		cam.FirstPersonExited.Connect(OnFirstPersonExited);
		CharacterChanged.Connect(OnCharacterChanged);

		// Disable auto update, this will be updated manually
		Character?.AutoUpdateNetTransform = false;
	}

	// Emit when this player is ready, fired for everyone
	private void OnPlayerReady()
	{
		SetNetworkAuthority(PeerID);
		UpdatePlayerCollision();
		UpdatePlrReady();
	}

	internal void InvokeChatted(string msg)
	{
		Chatted.Invoke(msg);
	}

	private void OnFirstPersonEntered()
	{
		if (Character == null) return;
		Character.GDNode3D.Visible = false;
		Character._bubbleChat.Visible = false;
	}

	private void OnFirstPersonExited()
	{
		if (Character == null) return;
		Character.GDNode3D.Visible = true;
		Character._bubbleChat.Visible = true;
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
	public override void Respawn()
	{
		CharacterModel? oldChar = Character;
		InternalSpawn();
		if (Root.PlayerDefaults.CorpseDecay && !Root.PlayerDefaults.ReuseCharacter)
		{
			// Remove would-be corpse
			oldChar?.Destroy();
		}
	}

	private void InternalSpawn()
	{
		// Create character if absent
		if (Character is null || !Root.PlayerDefaults.ReuseCharacter)
		{
			// Insert default character on client
			if (Root.Network.NetworkMode == NetworkService.NetworkModeEnum.Client)
			{
				Character = Root.Insert.DefaultNPCCharacter();
				// Create inventory
				if (Root.Network.IsServer && Character is not null)
				{
					Inventory? inv = this.FindChild<Inventory>("Inventory");
					if (inv is null)
					{
						Inventory inventory = Globals.LoadInstance<Inventory>(Root);
						inventory.NameOverride = "Inventory";
						inventory.NetworkParent = this;
						Character.Inventory = inventory;
					}
					else
					{
						Character.Inventory = inv;
					}
				}
			}
		}
		else
		{
			CharacterModel oldChar = Character;
			Character = null;
			Character = oldChar;
		}

		// Apply playerdefaults
		if (Character != null)
		{
			Character.MaxHealth = Root.PlayerDefaults.MaxHealth;
			Character.WalkSpeed = Root.PlayerDefaults.WalkSpeed;
			Character.SprintSpeed = Root.PlayerDefaults.SprintSpeed;
			Character.UseStamina = Root.PlayerDefaults.UseStamina;
			Character.Stamina = Root.PlayerDefaults.Stamina;
			Character.MaxStamina = Root.PlayerDefaults.MaxStamina;
			Character.StaminaRegen = Root.PlayerDefaults.StaminaRegen;
			Character.StaminaBurn = Root.PlayerDefaults.StaminaBurn;
			Character.JumpPower = Root.PlayerDefaults.JumpPower;
			Character.KeepInventory = Root.PlayerDefaults.KeepInventory;
			Character.UseHeadTurning = Root.PlayerDefaults.UseHeadTurning;
			Character.UseBubbleChat = Root.PlayerDefaults.UseBubbleChat;
			if (Character is PolytorianModel ptmodel)
			{
				ptmodel.StopRagdoll();
				ptmodel.ResetAppearance();
			}
			Character.Velocity = Vector3.Zero;
			Character.WarpToSpawnPoint();
			Character.Health = Character.MaxHealth;
			Character.Anchored = false;
		}
		RespawnTime = Root.PlayerDefaults.RespawnTime;
		AutoLoadAppearance = Root.PlayerDefaults.AutoLoadAppearance;
		MovementMode = Root.PlayerDefaults.MovementMode;

		// Clear & Re-copy inventory
		CopyInventory();

		Rpc(nameof(NetRespawned));
	}

	private void CopyInventory()
	{
		// Only allow this operation in server
		if (!Root.Network.IsServer) return;
		if (Character == null) return;
		if (Character.Inventory == null) return;

		if (!Character.KeepInventory)
		{
			foreach (Instance item in Character.Inventory.GetChildren())
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
					i.Parent = Character.Inventory;
				}
			}
		}
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetRespawned()
	{
		Respawned?.Invoke();

		Character?.OverrideCanCollide = false;
		Character?.UpdateCollision();
		Character?.IsDead = false;
	}

	internal void OnCharacterChanged(CharacterModel? oldChar = null)
	{
		oldChar?.AutoUpdateNetTransform = true;
		// Disable auto update, this will be updated manually
		Character?.AutoUpdateNetTransform = false;
		UpdatePlayerCollision();

		Camera? curcam = Root.Environment.CurrentCamera;
		if (curcam is not null && curcam.Mode == Camera.CameraModeEnum.Follow && Root.PlayerDefaults.AutoCameraFollow)
		{
			curcam.Target = Character?.Head ?? Character;
		}
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
