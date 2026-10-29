// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Scripting;
using Polytoria.Shared;
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
	private readonly List<Control> _leftWidgets = new();
	private readonly List<Control> _leftWatched = new();
	private Control? _userCardPivot;
	private Control? _playerlistRoot;

	private const float PlayerlistCloseOffsetX = 240f; // Matches playerlist's "close" animation

	public CoreUIRoot CoreUI = null!;

	public PTSignal CtrlLockCursorChanged { get; private set; } = new();

	[ScriptProperty]
	public PTSignal TopbarBoundsChanged { get; private set; } = new();

	[ScriptProperty]
	public Vector2 TopbarBounds { get; private set; } = Vector2.Zero;

	[ScriptProperty, ScriptLegacyProperty("TopInset")]
	public static float TopbarInset => 80;

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

		base.Init();
	}

	private void OnLayoutChanged() => Callable.From(RecomputeTopbarBounds).CallDeferred();

	private static bool IsWidget(Control control)
	{
		if (control is Button or TextureRect or PanelContainer or Label) return true;
		foreach (Node child in control.GetChildren())
		{
			if (child is Control) return false;
		}
		return true;
	}

	private void CollectLeftWidgets(Control container)
	{
		_leftWatched.Add(container);
		container.VisibilityChanged += OnLayoutChanged;
		container.ItemRectChanged += OnLayoutChanged;
		container.ChildEnteredTree += _ =>
		{
			RefreshLeftButtons();
			OnLayoutChanged();
		};
		container.ChildExitingTree += _ =>
		{
			RefreshLeftButtons();
			OnLayoutChanged();
		};

		foreach (Node child in container.GetChildren())
		{
			if (child is not Control control) continue;

			if (IsWidget(control))
			{
				_leftWidgets.Add(control);
				_leftWatched.Add(control);
				control.VisibilityChanged += OnLayoutChanged;
				control.ItemRectChanged += OnLayoutChanged;
			}
			else
			{
				CollectLeftWidgets(control);
			}
		}
	}

	private void RefreshLeftButtons()
	{
		foreach (Control control in _leftWatched)
		{
			control.VisibilityChanged -= OnLayoutChanged;
			control.ItemRectChanged -= OnLayoutChanged;
		}
		_leftWidgets.Clear();
		_leftWatched.Clear();

		Control? hbox = CoreUI.MenuButton.GetParent() as Control;
		if (hbox == null) return;

		CollectLeftWidgets(hbox);
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
			Callable.From(RecomputeTopbarBounds).CallDeferred();
		}
	}

	private void RecomputeTopbarBounds()
	{
		if (CoreUI == null) return;

		float leftEnd = CoreUI.MenuButton.GlobalPosition.X;
		foreach (Control control in _leftWidgets)
		{
			if (control.IsVisibleInTree())
			{
				leftEnd = Math.Max(leftEnd, control.GlobalPosition.X + control.Size.X);
			}
		}

		float rightStart;
		if (_useLeaderboard && _userCardPivot != null)
		{
			float trueScreenEdge = _playerlistRoot != null ? _playerlistRoot.GlobalPosition.X + _playerlistRoot.Size.X + 16f : GDNode.GetViewport().GetVisibleRect().Size.X;
			float baseX = CoreUI.UserCard.GlobalPosition.X - _userCardPivot.Position.X;
			float progress = Mathf.Clamp(_userCardPivot.Position.X / PlayerlistCloseOffsetX, 0f, 1f);
			rightStart = Mathf.Lerp(baseX, trueScreenEdge, progress);
		}
		else
		{
			rightStart = GDNode.GetViewport().GetVisibleRect().Size.X;
		}

		Vector2 newBounds = new(leftEnd, rightStart);
		if (newBounds == TopbarBounds) return;

		TopbarBounds = newBounds;
		TopbarBoundsChanged.Invoke();
		OnPropertyChanged(nameof(TopbarBounds));
	}

	public override void Ready()
	{
		RefreshCoreUIsVisibility();
		base.Ready();
	}

	public override void PreDelete()
	{
		if (CoreUI != null)
		{
			CoreUI.GetViewport().SizeChanged -= OnLayoutChanged;
			_userCardPivot?.ItemRectChanged -= OnLayoutChanged;
			foreach (Control control in _leftWatched)
			{
				control.VisibilityChanged -= OnLayoutChanged;
				control.ItemRectChanged -= OnLayoutChanged;
			}
		}
		base.PreDelete();
	}

	private void OnGameLoaded()
	{
		if (Root.Network.IsServer || Root.SessionType != World.SessionTypeEnum.Client) { return; }

		CoreUIRoot coreUI = Globals.CreateInstanceFromScene<CoreUIRoot>(CoreUIPath);
		coreUI.Root = Root;
		coreUI.Service = this;
		CoreUI = coreUI;
		GDNode.AddChild(coreUI, true, Godot.Node.InternalMode.Front);

		// Recompute whenever the viewport is resized
		coreUI.GetViewport().SizeChanged += OnLayoutChanged;

		// Track left side buttons
		RefreshLeftButtons();
		Control? hboxNode = CoreUI.MenuButton.GetParent() as Control;
		if (hboxNode != null)
		{
			hboxNode.ChildEnteredTree += _ =>
			{
				RefreshLeftButtons();
				OnLayoutChanged();
			};
			hboxNode.ChildExitingTree += _ =>
			{
				RefreshLeftButtons();
				OnLayoutChanged();
			};
		}

		// Track UserCard's position as it animates
		_userCardPivot = CoreUI.UserCard.GetParentOrNull<Control>();
		if (_userCardPivot != null)
		{
			_userCardPivot.ItemRectChanged += OnLayoutChanged;
			_playerlistRoot = _userCardPivot.GetParentOrNull<Control>()?.GetParentOrNull<Control>();
		}

		RefreshCoreUIsVisibility();
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
