// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Datamodel.Resources;
using Polytoria.Scripting;
using Polytoria.Shared;
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

	private CtrlLockCursorEnum _ctrlLockCursor = CtrlLockCursorEnum.Chevron;
	private CursorAsset? _defaultCursorOverride;
	private CursorAsset? _pointerCursorOverride;
	private CursorAsset? _grabCursorOverride;
	private CursorAsset? _grabbingCursorOverride;
	private CursorAsset? _crosshairCursorOverride;

	public CoreUIRoot CoreUI = null!;

	public PTSignal CtrlLockCursorChanged { get; private set; } = new();

	[Editable, ScriptProperty]
	public CtrlLockCursorEnum CtrlLockCursor
	{
		get => _ctrlLockCursor;
		set
		{
			_ctrlLockCursor = value;
			RefreshCoreUIsVisibility();
			OnPropertyChanged();
			CtrlLockCursorChanged.Invoke();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? DefaultCursorOverride
	{
		get { return _defaultCursorOverride; }
		set
		{
			SetMouseCursor(ref _defaultCursorOverride, value, Input.CursorShape.Arrow);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? PointerCursorOverride
	{
		get { return _pointerCursorOverride; }
		set
		{
			SetMouseCursor(ref _pointerCursorOverride, value, Input.CursorShape.PointingHand);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? GrabCursorOverride
	{
		get { return _grabCursorOverride; }
		set
		{
			SetMouseCursor(ref _grabCursorOverride, value, Input.CursorShape.Drag);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? GrabbingCursorOverride
	{
		get { return _grabbingCursorOverride; }
		set
		{
			SetMouseCursor(ref _grabbingCursorOverride, value, Input.CursorShape.CanDrop);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public CursorAsset? CrosshairCursorOverride
	{
		get { return _crosshairCursorOverride; }
		set
		{
			_crosshairCursorOverride = value;
			OnPropertyChanged();
		}
	}

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
			CoreUI.InventoryButton.Visible = UseBackpack;
			if (!UseBackpack)
			{
				CoreUI.Inventory.CloseBackpack();
				CoreUI.InventoryButton.SetPressedNoSignal(false);
			}
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

	private void SetMouseCursor(ref CursorAsset? cursor, CursorAsset? newCursor, Input.CursorShape shape)
	{
		if (cursor != null && cursor != newCursor)
		{
			// Wish there was a more optimal method for this. Oh well!
			cursor.CursorAdjustInternal.Disconnect(ReloadCursors);
			cursor.UnlinkFrom(this);
		}

		cursor = newCursor;

		if (cursor != null)
		{
			cursor.LinkTo(this);
			cursor.CursorAdjustInternal.Connect(ReloadCursors);
			if (!cursor.IsResourceLoaded)
				cursor.QueueLoadResource();
		}
		
		LoadMouseCursor(shape, cursor);
	}

	private void ReloadCursors()
	{
		LoadMouseCursor(Input.CursorShape.Arrow, DefaultCursorOverride);
		LoadMouseCursor(Input.CursorShape.PointingHand, PointerCursorOverride);
		LoadMouseCursor(Input.CursorShape.Drag, GrabCursorOverride);
		LoadMouseCursor(Input.CursorShape.CanDrop, GrabbingCursorOverride);
	}

	private void LoadMouseCursor(Input.CursorShape shape, CursorAsset? cursor = null)
	{
		if (Root == null) return;
		if (Root.SessionType != World.SessionTypeEnum.Client) return;

		if (cursor != null)
		{
			if (cursor is PTCursorAsset cursorImage)
			{
				void apply(Resource? res)
				{
					cursorImage.ResourceLoaded -= apply;
					Input.SetCustomMouseCursor(res, shape, cursorImage.Hotspot);
				}

				if (cursorImage.IsResourceLoaded && cursorImage.Resource != null)
				{
					apply(cursorImage.Resource);
				}
				else
				{
					cursorImage.ResourceLoaded += apply;
					cursorImage.QueueLoadResource();
				}
			}

			return;
		}

		// Load default cursors if no custom cursor is provided.
		// Default to loading the arrow cursor.
		Image defaultCursorImage = shape switch
		{
			Input.CursorShape.PointingHand => GD.Load<Image>("res://assets/textures/client/cursor/click.png"),
			Input.CursorShape.Drag => GD.Load<Image>("res://assets/textures/client/cursor/grab.png"),
			Input.CursorShape.CanDrop => GD.Load<Image>("res://assets/textures/client/cursor/grabbing.png"),
			_ => GD.Load<Image>("res://assets/textures/client/cursor/arrow.png"),
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
