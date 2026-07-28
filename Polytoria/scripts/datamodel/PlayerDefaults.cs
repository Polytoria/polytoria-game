// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Static("PlayerDefaults")]
public sealed partial class PlayerDefaults : HiddenBase
{
	private float _respawnTime;
	private float _maxHealth;
	private float _jumpPower;
	private float _coyoteTime;
	private float _walkSpeed;
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
	private bool _loadAppearanceTools;
	private bool _keepInventory;
	private bool _useBubbleChat;
	private bool _chatColorsEnabled;
	private Color _chatColor;
	private Player.PlayerCollisionShapeEnum _collisionShape;
	private Player.PlayerMovementModeEnum _movementMode;

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
		set {
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
	public bool LoadAppearanceTools
	{
		get => _loadAppearanceTools;
		set
		{
			_loadAppearanceTools = value;
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
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool ChatColorsEnabled
	{
		get => _chatColorsEnabled;
		set
		{
			_chatColorsEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color ChatColor
	{
		get => _chatColor;
		set
		{
			_chatColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Player.PlayerCollisionShapeEnum CollisionShape
	{
		get => _collisionShape;
		set
		{
			_collisionShape = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Player.PlayerMovementModeEnum MovementMode
	{
		get => _movementMode;
		set
		{
			_movementMode = value;
			OnPropertyChanged();
		}
	}

	public Inventory? Inventory => FindChild<Inventory>("Inventory")!;

	public override void Init()
	{
		LoadDefaults();
		base.Init();
	}

	[ScriptMethod]
	public void LoadDefaults()
	{
		RespawnTime = 5.0f;
		MaxHealth = 100f;
		JumpPower = 36f;
		CoyoteTime = 0.15f;
		WalkSpeed = 16f;
		SprintSpeed = 25f;
		UseStamina = true;
		MaxStamina = 3f;
		StaminaBurn = 1.2f;
		StaminaRegen = 1.2f;
		CanMove = true;
		CanRespawn = true;
		AirControl = 1f;
		AirFriction = 1000f;
		GroundControl = 1f;
		GroundFriction = 1000f;
		UseHeadTurning = true;
		UseFootplanting = true;
		UseBuiltInSounds = true;
		UseFirstPersonViewmodel = true;
		AutoLoadAppearance = true;
		LoadAppearanceTools = true;
		KeepInventory = false;
		UseBubbleChat = true;
		ChatColorsEnabled = true;
		ChatColor = new Color(1, 1, 1);
		CollisionShape = Player.PlayerCollisionShapeEnum.Capsule;
		MovementMode = Player.PlayerMovementModeEnum.Default;
	}
}
