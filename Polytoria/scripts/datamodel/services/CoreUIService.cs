// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Datamodel.Resources;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("CoreUI")]
public sealed partial class CoreUIService : Instance
{
	private const string CoreUIPath = "res://scenes/client/ui/core_ui.tscn";

	private int _chatBubbleRenderDistance = 40;
	private bool _useUserCard = true;
	private bool _useChat = true;
	private bool _useHealthBar = true;
	private bool _useLeaderboard = true;
	private bool _useHotBar = true;
	private bool _useBackpack = true;
	private bool _useMenuButton = true;
	private bool _useEmoteWheel = true;
	private bool _canRespawn = true;

	private CtrlLockCursorEnum _ctrlLockCursor = CtrlLockCursorEnum.None;
	public PTSignal CrosshairChanged { get; private set; } = new();

	private Dictionary<Input.CursorShape, CursorAsset?> _cursorOverrides = new() {
		{Input.CursorShape.Arrow, null},
		{Input.CursorShape.PointingHand, null},
		{Input.CursorShape.Drag, null},
		{Input.CursorShape.CanDrop, null},
	};
	private Dictionary<CursorAsset, Action> _cursorCallbacks = new() { };
	private CursorAsset? _crosshairOverride;
	public CoreUIRoot CoreUI = null!;

	[Editable, ScriptProperty, Attributes.Obsolete("Use CrosshairOverride instead.")]
	public CtrlLockCursorEnum CtrlLockCursor
	{
		get => _ctrlLockCursor;
		set
		{
			_ctrlLockCursor = value;
			RefreshCoreUIsVisibility();
			OnPropertyChanged();
			CrosshairChanged.Invoke();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? DefaultCursorOverride
	{
		get { return _cursorOverrides[Input.CursorShape.Arrow]; }
		set
		{
			SetMouseCursor(Input.CursorShape.Arrow, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? PointerCursorOverride
	{
		get { return _cursorOverrides[Input.CursorShape.PointingHand]; }
		set
		{
			SetMouseCursor(Input.CursorShape.PointingHand, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? GrabCursorOverride
	{
		get { return _cursorOverrides[Input.CursorShape.Drag]; }
		set
		{
			SetMouseCursor(Input.CursorShape.Drag, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? GrabbingCursorOverride
	{
		get { return _cursorOverrides[Input.CursorShape.CanDrop]; }
		set
		{
			SetMouseCursor(Input.CursorShape.CanDrop, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? CrosshairOverride
	{
		get { return _crosshairOverride; }
		set
		{
			if (_crosshairOverride == value) return;

			if (_crosshairOverride != null)
			{
				if (_cursorCallbacks.TryGetValue(_crosshairOverride, out Action callback))
					_crosshairOverride.CursorAdjustInternal.Disconnect(callback);

				_crosshairOverride.UnlinkFrom(this);
			}

			_crosshairOverride = value;

			if (value != null)
			{
				Action onAdjust = () => CrosshairChanged.Invoke();
				_crosshairOverride.CursorAdjustInternal.Connect(onAdjust);
				_cursorCallbacks.Add(_crosshairOverride, onAdjust);

				_crosshairOverride.LinkTo(this);
				if (!_crosshairOverride.IsResourceLoaded)
					_crosshairOverride.QueueLoadResource();
			}

			CrosshairChanged.Invoke();
			OnPropertyChanged();
		}
	}

  [ScriptProperty]
	public static float TopInset => 80;

	[Editable, ScriptProperty]
	public int ChatBubbleRenderDistance
	{
		get => _chatBubbleRenderDistance;
		set { _chatBubbleRenderDistance = value; }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("UserCardEnabled")]
	public bool UseUserCard
	{
		get => _useUserCard;
		set { _useUserCard = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("ChatEnabled")]
	public bool UseChat
	{
		get => _useChat;
		set { _useChat = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("HealthBarEnabled")]
	public bool UseHealthBar
	{
		get => _useHealthBar;
		set { _useHealthBar = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("LeaderboardEnabled")]
	public bool UseLeaderboard
	{
		get => _useLeaderboard;
		set { _useLeaderboard = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("HotbarEnabled")]
	public bool UseHotbar
	{
		get => _useHotBar;
		set { _useHotBar = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public bool UseBackpack
	{
		get => _useBackpack;
		set { _useBackpack = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("MenuButtonEnabled")]
	public bool UseMenuButton
	{
		get => _useMenuButton;
		set { _useMenuButton = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public bool UseEmoteWheel
	{
		get => _useEmoteWheel;
		set { _useEmoteWheel = value; RefreshCoreUIsVisibility(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public bool CanRespawn
	{
		get => _canRespawn;
		set { _canRespawn = value; OnPropertyChanged(); }
	}

	public override void Init()
	{
		Root.Loaded.Once(OnGameLoaded);
		ReloadCursors();
		base.Init();
	}

	private void RefreshCoreUIsVisibility()
	{
		if (CoreUI != null)
		{
			CoreUI.UserCard.Visible = UseUserCard;
			CoreUI.Chat.Visible = UseChat;
			CoreUI.ChatButton.Visible = UseChat;
			CoreUI.HealthBar.Visible = UseHealthBar;
			CoreUI.Leaderboard.Visible = UseLeaderboard;
			CoreUI.Inventory.Visible = UseHotbar;
			CoreUI.MenuButton.Visible = UseMenuButton;
			CoreUI.EmoteWheel.UseEmoteWheel = UseEmoteWheel;
		}
	}

	public override void Ready()
	{
		RefreshCoreUIsVisibility();
		base.Ready();
	}

	private void OnGameLoaded()
	{
		if (Root.Network.IsServer || Root.SessionType != World.SessionTypeEnum.Client) return;

		CoreUIRoot coreUI = Globals.CreateInstanceFromScene<CoreUIRoot>(CoreUIPath);
		coreUI.Root = Root;
		coreUI.Service = this;
		CoreUI = coreUI;
		GDNode.AddChild(coreUI, true, Godot.Node.InternalMode.Front);
		RefreshCoreUIsVisibility();
		ReloadCursors();
	}

	internal async Task<CoreUIRoot> WaitRoot()
	{
		if (CoreUI != null)
		{
			return CoreUI;
		}

		while (CoreUI == null)
		{
			await Task.Delay(100);
		}

		return CoreUI;
	}

	private void SetMouseCursor(Input.CursorShape shape, CursorAsset? newCursor)
	{
		if (_cursorOverrides.TryGetValue(shape, out CursorAsset? currentCursor))
		{
			if (currentCursor == newCursor) return;

			if (currentCursor != null)
			{
				if (_cursorCallbacks.TryGetValue(currentCursor, out Action callback))
					currentCursor.CursorAdjustInternal.Disconnect(callback);

				currentCursor.UnlinkFrom(this);
			}

			_cursorOverrides[shape] = newCursor;

			if (newCursor != null)
			{
				Action onAdjust = () => LoadMouseCursor(shape, newCursor);
				newCursor.CursorAdjustInternal.Connect(onAdjust);
				_cursorCallbacks.Add(newCursor, onAdjust);

				newCursor.LinkTo(this);
				if (!newCursor.IsResourceLoaded)
					newCursor.QueueLoadResource();
			}

			LoadMouseCursor(shape, newCursor);
		}
	}

	private void ReloadCursors()
	{
		foreach (KeyValuePair<Input.CursorShape, CursorAsset?> pair in _cursorOverrides)
		{
			LoadMouseCursor(pair.Key, pair.Value);
		}
	}

	private void LoadMouseCursor(Input.CursorShape shape, CursorAsset? cursor = null)
	{
		if (Root == null) return;
		if (Root.SessionType != World.SessionTypeEnum.Client) return;

		if (cursor != null)
		{
			void apply(Image? img)
			{
				cursor.CursorLoaded -= apply;
				if (img != null)
					Input.SetCustomMouseCursor(img, shape, cursor.Hotspot * img.GetSize());
			}

			if (cursor.IsResourceLoaded)
			{
				apply(cursor.CursorImage);
			}
			else
			{
				cursor.CursorLoaded += apply;
				cursor.QueueLoadResource();
			}

			return;
		}

		// Load default cursors if no custom cursor is provided.
		// Default to loading the arrow cursor.
		DpiTexture defaultCursorImage = shape switch
		{
			Input.CursorShape.PointingHand => GD.Load<DpiTexture>("res://assets/textures/client/cursor/click.svg"),
			Input.CursorShape.Drag => GD.Load<DpiTexture>("res://assets/textures/client/cursor/grab.svg"),
			Input.CursorShape.CanDrop => GD.Load<DpiTexture>("res://assets/textures/client/cursor/grabbing.svg"),
			_ => GD.Load<DpiTexture>("res://assets/textures/client/cursor/arrow.svg"),
		};

		Input.SetCustomMouseCursor(defaultCursorImage, shape);
		return;
	}

	[ScriptEnum("CtrlLockCursor")]
	public enum CtrlLockCursorEnum
	{
		None,
		Chevron,
		Stereotypical,
		StereotypicalDot,
		Tactical,
		Dot,
		TacticalDot,
		Plus,
		X
	}
}
