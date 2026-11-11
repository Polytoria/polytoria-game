// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Static("PlayerDefaults")]
public sealed partial class PlayerDefaults : HiddenBase
{
	private float _maxHealth;
	private float _walkSpeed;
	private float _jumpPower;
	private Color _chatColor;
	private bool _chatColorsEnabled;
	private float _respawnTime;
	private bool _canMove;
	private float _sprintSpeed;
	private bool _useStamina;
	private float _maxStamina;
	private float _staminaRegen;
	private float _staminaBurn;
	private bool _useExhaustion;
	private float _exhaustionRegen;
	private float _exhaustionCeil;
	private bool _keepInventory;
	private bool _useHeadTurning;
	private bool _useBubbleChat;
	private bool _autoLoadAppearance;
	private bool _loadAppearanceTools;
	private Player.PlayerMovementModeEnum _movementMode;

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
	public bool ChatColorsEnabled
	{
		get => _chatColorsEnabled;
		set { _chatColorsEnabled = value; OnPropertyChanged(); }
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

	[Attributes.Obsolete("Use Player.Stamina instead.")]
	public float Stamina
	{
		get => 100f;
		set { }
	}

	[Editable, ScriptProperty]
	public float MaxStamina
	{
		get => _maxStamina;
		set
		{
			_maxStamina = Mathf.Max(value, 0f);
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
			_staminaBurn = Mathf.Max(value, 0f);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseExhaustion
	{
		get => _useExhaustion;
		set
		{
			_useExhaustion = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float ExhaustionRegen
	{
		get => _exhaustionRegen;
		set
		{
			_exhaustionRegen = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float ExhaustionCeil
	{
		get => _exhaustionCeil;
		set
		{
			_exhaustionCeil = Mathf.Clamp(value, 0f, 1f);
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
		MaxHealth = 100f;
		WalkSpeed = 16f;
		JumpPower = 36f;
		ChatColor = new Color(1, 1, 1);
		ChatColorsEnabled = true;
		RespawnTime = 5.0f;
		CanMove = true;
		SprintSpeed = 25f;
		MaxStamina = 3f;
		UseStamina = true;
		StaminaRegen = 1f / 1.2f;
		StaminaBurn = 1f / 1.2f;
		UseExhaustion = false;
		ExhaustionRegen = 1f / 0.6f;
		ExhaustionCeil = 0.5f;
		UseHeadTurning = true;
		UseBubbleChat = true;
		AutoLoadAppearance = true;
		LoadAppearanceTools = true;
		MovementMode = Player.PlayerMovementModeEnum.Default;
	}
}
