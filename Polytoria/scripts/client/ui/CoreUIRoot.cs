// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.UI.Capture;
using Polytoria.Client.UI.Chat;
using Polytoria.Client.UI.Playerlist;
using Polytoria.Client.UI.Purchases;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;

#if DEBUG && !EXPORTDEBUG
using Polytoria.Shared;
#endif

namespace Polytoria.Client.UI;

public partial class CoreUIRoot : CanvasLayer
{
	public static CoreUIRoot Singleton { get; private set; } = null!;
	public CoreUIRoot()
	{
		Singleton = this;
	}

	[ExportSubgroup("UI Elements")]
	[Export] public UIGameMenu GameMenu = null!;
	[Export] public UIMenuButton MenuButton = null!;
	[Export] public UIUserCard UserCard = null!;
	[Export] public UIChat Chat = null!;
	[Export] public UIChatButton ChatButton = null!;
	[Export] public UIHealthbar HealthBar = null!;
	[Export] public UILeaderboard Leaderboard = null!;
	[Export] public UIInventory Inventory = null!;
	[Export] public UIEmoteWheel EmoteWheel = null!;
	[Export] public UINotification NotificationCenter = null!;
	[Export] public UICapturePreview CapturePreview = null!;
	[Export] public UIPurchasePrompt PurchasePrompt = null!;
	[Export] public TextureRect CtrlLockCursor = null!;
	[Export] public DevConsoleWindow DevWindow = null!;

	[ExportSubgroup("Filepaths")]
	[Export] public string CtrlLockCursorsFilepath = null!;

	/// <summary>
	/// Determine if CoreUI has active popup, this overrides Input.IsGameFocused
	/// </summary>
	public bool CoreUIActive { get; set; } = false;

	public World Root { get; set; } = null!;
	public CoreUIService Service { get; set; } = null!;

	public override void _EnterTree()
	{
		// Assign CoreUI Root
		GameMenu.CoreUI = this;
		NotificationCenter.CoreUI = this;
		CapturePreview.CoreUI = this;
		Inventory.CoreUI = this;
		Leaderboard.CoreUI = this;
		Chat.CoreUI = this;
		ChatButton.CoreUI = this;
		HealthBar.CoreUI = this;
		PurchasePrompt.CoreUI = this;

#if DEBUG && !EXPORTDEBUG
		if (OS.HasFeature("executor"))
		{
			AddChild(Globals.CreateInstanceFromScene<Node>("res://scenes/client/ui/executor/executor.tscn"));
		}
#endif

		Service.CtrlLockCursorChanged.Connect(OnCtrlLockCursorChanged);

		base._EnterTree();
		OnCtrlLockCursorChanged();
	}

	public override void _ExitTree()
	{
		Service.CtrlLockCursorChanged.Disconnect(OnCtrlLockCursorChanged);
		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		SyncCtrlLockCursor();
		base._Process(delta);
	}

	private void SyncCtrlLockCursor()
	{
		if (CtrlLockCursor != null)
		{
			CtrlLockCursor.Visible = Root?.Environment?.CurrentCamera?.CtrlLocked == true;
		}
	}

	private void OnCtrlLockCursorChanged()
	{
		string filename = "";
		switch (Service.CtrlLockCursor)
		{
			case CoreUIService.CtrlLockCursorEnum.StereotypicalDot:
				filename = "stereotypical-dot.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.Stereotypical:
				filename = "stereotypical.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.Tactical:
				filename = "tactical.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.TacticalDot:
				filename = "tactical-dot.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.Dot:
				filename = "dot.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.Plus:
				filename = "plus.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.X:
				filename = "plus.svg";
				break;
			case CoreUIService.CtrlLockCursorEnum.Chevron:
				filename = "chevron.svg";
				break;
		}

		if (Service.CtrlLockCursor == CoreUIService.CtrlLockCursorEnum.None)
		{
			CtrlLockCursor.Texture = null;
			return;
		}
		var dpiTexture = GD.Load<DpiTexture>(CtrlLockCursorsFilepath + "/" + filename);
		CtrlLockCursor.Texture = dpiTexture;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("hide_ui"))
		{
			Visible = !Visible;
		}
		if (@event.IsActionPressed("toggle_console"))
		{
			DevWindow.Toggle();
		}
		base._UnhandledInput(@event);
	}
}
