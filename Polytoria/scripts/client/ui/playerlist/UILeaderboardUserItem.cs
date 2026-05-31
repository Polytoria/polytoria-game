// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Schemas.API;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Playerlist;

public partial class UILeaderboardUserItem : Button
{
	private readonly Dictionary<Stat, Label> _statToLabel = [];

	[Export] private Label _usernameLabel = null!;
	[Export] private Control _statsBox = null!;
	[Export] private TextureRect _badgeRect = null!;

	public Player TargetPlayer = null!;
	public UILeaderboard Leaderboard = null!;

	public override void _Ready()
	{
		_usernameLabel.Text = TargetPlayer.Name;

		if (TargetPlayer.UserInfo.HasValue)
		{
			UpdateUserInfo(TargetPlayer.UserInfo.Value);
		}
		else
		{
			TargetPlayer.UserInfoReady += UpdateUserInfo;
		}

		TargetPlayer.StatChanged.Connect(OnStatChanged);

		UpdateBadge();
	}

	private void OnStatChanged(Stat s, object? _)
	{
		UpdateStat(s);
	}

	public override void _ExitTree()
	{
		TargetPlayer.UserInfoReady -= UpdateUserInfo;
		TargetPlayer.StatChanged.Disconnect(OnStatChanged);
		base._ExitTree();
	}

	public void AddStat(Stat stat)
	{
		Label l = new()
		{
			CustomMinimumSize = new(70, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
		};
		_statsBox.AddChild(l);
		_statToLabel[stat] = l;
		UpdateStat(stat);
	}

	public void UpdateStat(Stat stat)
	{
		_statToLabel[stat].Text = stat.GetDisplayValue(TargetPlayer);

		Leaderboard.QueueSortList();
	}

	private void UpdateUserInfo(APIUserInfo info)
	{
		TargetPlayer.UserInfoReady -= UpdateUserInfo;
		UpdateBadge();
	}

	private void UpdateBadge()
	{
		string badgePath = Player.GetBadgeIconPath(TargetPlayer);
		if (badgePath.Length > 0)
			_badgeRect.Texture = GD.Load<Texture2D>(badgePath);
	}

	public override void _Pressed()
	{
		Leaderboard.UserOptions.PopupAt(this);
		base._Pressed();
	}
}
