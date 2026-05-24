// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.UI.Playerlist.Stats;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Playerlist;

public partial class UIUserCard : Control
{
	private const string UserCardStat = "res://scenes/client/ui/playerlist/stats/user_card_stat.tscn";

	private readonly Dictionary<Stat, UIUserCardStat> _statToUserCardStat = [];

	[Export] private Label _usernameLabel = null!;
	[Export] private Control _statsContainer = null!;
	[Export] private TextureRect _pfpIconRect = null!;
	[Export] private TextureRect _badgeRect = null!;
	private readonly PTImageAsset _plrIconAsset = new();
	private static World Root => CoreUIRoot.Singleton.Root;
	internal static Player TargetPlayer => Root.Players.LocalPlayer;

	public override void _Ready()
	{
		_usernameLabel.Text = Root.Players.LocalPlayer.Name;

		_plrIconAsset.ResourceLoaded += OnIconLoaded;
		_plrIconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_plrIconAsset.ImageID = (uint)TargetPlayer.UserID;
		_plrIconAsset.LoadResource();

		Root.Stats.StatAdded.Connect(AddStat);
		Root.Stats.StatRemoved.Connect(RemoveStat);

		if (TargetPlayer.UserInfo.HasValue)
		{
			LoadBadge();
		}
		else
		{
			TargetPlayer.UserInfoReady += OnUserInfoReady;
		}

		foreach (var item in Root.Stats.GetChildren())
		{
			if (item is Stat stat)
			{
				AddStat(stat);
			}
		}
	}

	public override void _ExitTree()
	{
		_plrIconAsset.ResourceLoaded -= OnIconLoaded;
		Root.Stats.StatAdded.Disconnect(AddStat);
		Root.Stats.StatRemoved.Disconnect(RemoveStat);
		TargetPlayer.UserInfoReady -= OnUserInfoReady;

		base._ExitTree();
	}

	private void AddStat(Stat stat)
	{
		if (_statToUserCardStat.ContainsKey(stat)) return;
		var s = Globals.CreateInstanceFromScene<UIUserCardStat>(UserCardStat);
		s.TargetStat = stat;
		s.Root = this;
		_statsContainer.AddChild(s);
		_statToUserCardStat.Add(stat, s);

		void OnStatDeleted()
		{
			stat.Deleted -= OnStatDeleted;
			RemoveStat(stat);
		}

		stat.Deleted += OnStatDeleted;
		RefreshBox();
	}

	private void RemoveStat(Stat stat)
	{
		if (_statToUserCardStat.TryGetValue(stat, out var statUI))
		{
			statUI.QueueFree();
			_statToUserCardStat.Remove(stat);
			RefreshBox();
		}
	}

	private void OnIconLoaded(Resource resource)
	{
		_pfpIconRect.Texture = (Texture2D)resource;
	}

	private void OnUserInfoReady(APIUserInfo _)
	{
		TargetPlayer.UserInfoReady -= OnUserInfoReady;
		LoadBadge();
	}

	private void LoadBadge()
	{
		string badgePath = Player.GetBadgeIconPath(TargetPlayer);
		if (badgePath.Length > 0)
			_badgeRect.Texture = GD.Load<Texture2D>(badgePath);
	}

	private async void RefreshBox()
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
	}
}
