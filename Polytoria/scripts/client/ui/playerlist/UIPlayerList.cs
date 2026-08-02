// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Playerlist;

public partial class UIPlayerList : Node
{
	[Export] private AnimationPlayer _leaderboardAnim = null!;

	private bool _shown = true;
	public bool IsLeaderboardShown => _shown;

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_leaderboard"))
		{
			ToggleLeaderboard();
		}
		base._UnhandledKeyInput(@event);
	}

	public void Open()
	{
		if (_shown) return;
		_shown = true;
		_leaderboardAnim.Stop();
		_leaderboardAnim.Play("open");
	}

	public void Close()
	{
		if (!_shown) return;
		_shown = false;
		_leaderboardAnim.Stop();
		_leaderboardAnim.Play("close");
	}

	public void ToggleLeaderboard()
	{
		_shown = !_shown;
		_leaderboardAnim.Stop();
		_leaderboardAnim.Play(_shown ? "open" : "close");
	}
}
