// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.UI.Capture;
using Polytoria.Client.UI.Chat;
using Polytoria.Client.UI.Playerlist;
using Polytoria.Client.UI.Purchases;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;

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
	[Export] public UIInventoryButton InventoryButton = null!;
	[Export] public UIEmoteWheel EmoteWheel = null!;
	[Export] public UINotification NotificationCenter = null!;
	[Export] public UICapturePreview CapturePreview = null!;
	[Export] public UIPurchasePrompt PurchasePrompt = null!;
	[Export] public TextureRect Crosshair = null!;
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
		InventoryButton.CoreUI = this;
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

		Service.CrosshairChanged.Connect(OnCrosshairChanged);

		base._EnterTree();
		OnCrosshairChanged();
	}

	public override void _ExitTree()
	{
		Service.CrosshairChanged.Disconnect(OnCrosshairChanged);
		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		SyncCrosshair();
		base._Process(delta);
	}

	private void SyncCrosshair()
	{
		Crosshair?.Visible = Root?.Environment?.CurrentCamera?.CtrlLocked == true;
	}

	private void OnCrosshairChanged()
	{
		if (Service.CrosshairOverride is CursorAsset c)
		{
			void apply(Image? img)
			{
				c.CursorLoaded -= apply;
				if (img != null)
				{
					ImageTexture tex = ImageTexture.CreateFromImage(img);
					Vector2 texSize = tex.GetSize();
					Crosshair.Texture = tex;
					Crosshair.Size = texSize;
					Crosshair.OffsetTransformPosition = -c.Hotspot * texSize;
				}
				else
				{
					Crosshair.Texture = null;
				}
			}

			if (c.IsResourceLoaded)
			{
				apply(c.CursorImage);
			}
			else
			{
				c.CursorLoaded += apply;
				c.QueueLoadResource();
			}
		}
		else
		{
			if (Service.CtrlLockCursor == CoreUIService.CtrlLockCursorEnum.None)
			{
				Crosshair.Texture = null;
				return;
			}

			string filename = Service.CtrlLockCursor switch
			{
				CoreUIService.CtrlLockCursorEnum.StereotypicalDot => "crosshair-vertical-dot.svg",
				CoreUIService.CtrlLockCursorEnum.Stereotypical => "crosshair-vertical.svg",
				CoreUIService.CtrlLockCursorEnum.Tactical => "crosshair-tactical.svg",
				CoreUIService.CtrlLockCursorEnum.TacticalDot => "crosshair-tactical-dot.svg",
				CoreUIService.CtrlLockCursorEnum.Dot => "dot.svg",
				CoreUIService.CtrlLockCursorEnum.Plus => "plus.svg",
				CoreUIService.CtrlLockCursorEnum.X => "x.svg",
				CoreUIService.CtrlLockCursorEnum.Chevron => "chevron.svg",
				_ => "",
			};
			Crosshair.Texture = GD.Load<DpiTexture>(Globals.BuiltInCursorLocation.PathJoin(filename));
		}
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
