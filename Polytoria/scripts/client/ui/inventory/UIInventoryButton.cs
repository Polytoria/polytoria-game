// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIInventoryButton : Button
{
	[Export] public UIInventory InventoryUI { get; set; } = null!;

	public override void _Ready()
	{
		Toggled += OnToggled;
	}

	internal void OnToggled(bool toggleOn)
	{
		if (toggleOn)
		{
			InventoryUI.OpenBackpack();
		}
		else
		{
			InventoryUI.CloseBackpack();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_backpack"))
		{
			SetPressedNoSignal(!ButtonPressed);
		}
		base._UnhandledInput(@event);
	}
}
