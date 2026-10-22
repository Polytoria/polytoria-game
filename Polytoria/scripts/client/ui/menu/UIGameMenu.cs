// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public partial class UIGameMenu : Control
{
	private const float MobileScreenMargin = 8f;

	public Vector2 GameMenuSize = new(960, 524);

	[Export] public bool ForceMobileLayout;

	private readonly Dictionary<GameMenuViewEnum, UIMenuViewBase> _loadedViews = [];
	private UIMenuViewBase? _currentView = null;

	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private Control _viewContainer = null!;
	[Export] private Control _firstFocus = null!;
	[Export] private Control _gameMenuPanel = null!;

	public bool IsShowing = false;
	public bool UseMobileLayout => Globals.IsMobileBuild || ForceMobileLayout;

	public CoreUIRoot CoreUI = null!;
	public event Action<bool>? IsShowingChanged;
	public event Action<GameMenuViewEnum>? ViewChanged;
	private readonly List<UIMenuTabButton> _tabButtons = [];

	public override void _Ready()
	{
		Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_menu"))
		{
			ToggleMenu();
		}
		base._UnhandledInput(@event);
	}

	public void RegisterTabButton(UIMenuTabButton tabbtn)
	{
		_tabButtons.Add(tabbtn);
	}

	public void ToggleMenu()
	{
		if (IsShowing)
		{
			HideMenu();
		}
		else
		{
			ShowMenu();
		}
	}

	public void ShowMenu()
	{
		if (IsShowing) { return; }
		IsShowing = true;
		_animPlay.Play("appear");
		Visible = true;
		_firstFocus.GrabFocus();
		IsShowingChanged?.Invoke(IsShowing);
		CoreUIRoot.Singleton.Root.Input.IsMenuOpened = true;
		SwitchView(GameMenuViewEnum.Overview);
		RefreshSize();
	}

	private void RefreshSize()
	{
		if (UseMobileLayout)
		{
			FitToScreen();
			return;
		}

		Rect2 rect = GetViewportRect();
		if (rect.Size.X < GameMenuSize.X)
		{
			_gameMenuPanel.Size = new(rect.Size.X, _gameMenuPanel.Size.Y);
		}
		else
		{
			_gameMenuPanel.Size = new(GameMenuSize.X, _gameMenuPanel.Size.Y);
		}
		if (rect.Size.Y < GameMenuSize.Y)
		{
			_gameMenuPanel.Size = new(_gameMenuPanel.Size.X, rect.Size.Y);
		}
		else
		{
			_gameMenuPanel.Size = new(_gameMenuPanel.Size.X, GameMenuSize.Y);
		}
		_gameMenuPanel.SetDeferred(Control.PropertyName.AnchorsPreset, (int)LayoutPreset.Center);
	}

	private void FitToScreen()
	{
		Rect2 viewport = GetViewportRect();
		Rect2 area = GetSafeArea().Grow(-MobileScreenMargin);
		float scale = Mathf.Min(1f, Mathf.Min(area.Size.X / GameMenuSize.X, area.Size.Y / GameMenuSize.Y));

		_gameMenuPanel.Size = GameMenuSize;
		_gameMenuPanel.SetDeferred(Control.PropertyName.AnchorsPreset, (int)LayoutPreset.Center);

		Control holder = _gameMenuPanel.GetParent<Control>();
		holder.Scale = new(scale, scale);
		holder.Position = area.GetCenter() - viewport.GetCenter();
	}

	private Rect2 GetSafeArea()
	{
		Rect2 viewport = GetViewportRect();
		if (!Globals.IsMobileBuild)
		{
			return viewport;
		}

		Rect2I safe = DisplayServer.GetDisplaySafeArea();
		Vector2 windowSize = DisplayServer.WindowGetSize();
		if (safe.Size.X <= 0 || windowSize.X <= 0)
		{
			return viewport;
		}

		float toCanvas = viewport.Size.X / windowSize.X;
		return new Rect2((Vector2)safe.Position * toCanvas, (Vector2)safe.Size * toCanvas).Intersection(viewport);
	}

	public void HideMenu()
	{
		if (!IsShowing) { return; }
		GetViewport().GuiReleaseFocus();
		IsShowing = false;
		_animPlay.Stop(true);
		_animPlay.Play("disappear");
		CoreUIRoot.Singleton.Root.Input.IsMenuOpened = false;
		IsShowingChanged?.Invoke(IsShowing);
		_currentView?.HideView();
	}

	public void SwitchView(GameMenuViewEnum switchTo)
	{
		// Hide the current view if it exists
		if (_currentView != null)
		{
			_currentView.Visible = false;
			_currentView.HideView();
		}

		// Check if the view is already loaded
		if (!_loadedViews.TryGetValue(switchTo, out UIMenuViewBase? view))
		{
			string pathToLoad = switchTo switch
			{
				GameMenuViewEnum.Overview => "res://scenes/client/ui/menu/views/overview.tscn",
				GameMenuViewEnum.Players => "res://scenes/client/ui/menu/views/players.tscn",
				GameMenuViewEnum.Report => "res://scenes/client/ui/menu/views/report.tscn",
				GameMenuViewEnum.Settings => "res://scenes/client/ui/menu/views/settings.tscn",
				_ => throw new ArgumentOutOfRangeException(nameof(switchTo), $"No scene defined for {switchTo}")
			};

			PackedScene scene = GD.Load<PackedScene>(pathToLoad);
			if (scene != null)
			{
				view = scene.Instantiate<UIMenuViewBase>();
				_viewContainer.AddChild(view);
				_loadedViews[switchTo] = view;
			}
			else
			{
				PT.PrintErr("Failed to load settings scene at: " + pathToLoad);
				return;
			}
		}

		// Update first focus
		foreach (UIMenuTabButton tabBtn in _tabButtons)
		{
			if (view.FirstFocus != null)
			{
				tabBtn.FocusNeighborBottom = tabBtn.GetPathTo(view.FirstFocus);
			}
		}

		// Show the new view
		view.Menu = this;
		view.ShowView();
		view.Visible = true;
		ViewChanged?.Invoke(switchTo);
		_currentView = view;
	}

	public enum GameMenuViewEnum
	{
		Overview,
		Players,
		Report,
		Settings
	}
}
