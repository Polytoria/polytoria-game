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
	[Export] public DevConsoleWindow DevWindow = null!;

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

		base._EnterTree();
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
