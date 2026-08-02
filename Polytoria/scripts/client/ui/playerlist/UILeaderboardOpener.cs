// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Playerlist;

public partial class UILeaderboardOpener : Control
{
	[Export] private UIPlayerList _playerList = null!;

	private void OpenLB()
	{
		if (_playerList == null)
			return;

		if (!_playerList.IsLeaderboardShown)
		{
			_playerList.Open();
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
		{
			OpenLB();
			AcceptEvent();
		}
		else if (@event is InputEventScreenTouch touch && !touch.Pressed)
		{
			OpenLB();
			AcceptEvent();
		}
	}
}
