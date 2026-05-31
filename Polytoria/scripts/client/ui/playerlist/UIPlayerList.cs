// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Playerlist;

public partial class UIPlayerList : Node
{
	[Export] private AnimationPlayer _leaderboardAnim = null!;

	private bool IsLeaderboardShown = true;

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_leaderboard"))
		{
			ToggleLeaderboard();
		}
		base._UnhandledKeyInput(@event);
	}

	private void ToggleLeaderboard()
	{
		IsLeaderboardShown = !IsLeaderboardShown;
		_leaderboardAnim.Stop();
		_leaderboardAnim.Play(IsLeaderboardShown ? "open" : "close");
	}

}
