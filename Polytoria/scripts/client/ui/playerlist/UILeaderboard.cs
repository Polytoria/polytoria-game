// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Client.UI.Playerlist;

public partial class UILeaderboard : TouchScrollContainer
{
	private const int LeaderboardMaxHeight = 300;
	private const string ItemPath = "res://scenes/client/ui/playerlist/leaderboard_user_item.tscn";
	private const string TeamItemPath = "res://scenes/client/ui/playerlist/leaderboard_team_item.tscn";

	[Export] private Control _container = null!;
	[Export] private Control _layout = null!;
	[Export] private HBoxContainer _headerRow = null!;
	[Export] private UIUserCard _userCard = null!;
	[Export] public UILeaderboardUserOptions UserOptions = null!;
	public CoreUIRoot CoreUI = null!;

	private Datamodel.Stats Stats => CoreUI.Root.Stats;
	private Teams Teams => CoreUI.Root.Teams;
	private World World => CoreUI.Root;

	private readonly Dictionary<Player, UILeaderboardUserItem> _playerToItem = [];
	private readonly Dictionary<Team, UILeaderboardTeamItem> _teamToItem = [];
	private UILeaderboardTeamItem? _neutralTeamItem;
	private Players _players = null!;
	private bool _queueResort = false;

	public override void _Ready()
	{
		_players = CoreUI.Root.Players;

		_players.PlayerAdded.Connect(AddPlayer);
		_players.PlayerRemoved.Connect(RemovePlayer);

		Stats.StatAdded.Connect(OnStatDefinitionChanged);
		Stats.StatPropertyChanged.Connect(OnStatDefinitionChanged);
		Stats.StatRemoved.Connect(OnStatDefinitionChanged);

		Teams.TeamAdded.Connect(TeamChanged);
		Teams.TeamRemoved.Connect(TeamChanged);

		Teams.TeamUpdateDispatch += QueueSortList;

		VisibilityChanged += () => LeaderboardUpdate();

		CreateHeader();
		Refresh();
	}

	public override void _ExitTree()
	{
		_players.PlayerAdded.Disconnect(AddPlayer);
		_players.PlayerRemoved.Disconnect(RemovePlayer);

		Stats.StatAdded.Disconnect(OnStatDefinitionChanged);
		Stats.StatPropertyChanged.Disconnect(OnStatDefinitionChanged);
		Stats.StatRemoved.Disconnect(OnStatDefinitionChanged);

		Teams.TeamAdded.Disconnect(TeamChanged);
		Teams.TeamRemoved.Disconnect(TeamChanged);

		Teams.TeamUpdateDispatch -= QueueSortList;

		foreach (var player in _playerToItem.Keys)
		{
			player.StatChanged.Disconnect(OnPlayerStatChanged);
			player.TeamChanged.Disconnect(OnPlayerTeamChanged);
		}

		_neutralTeamItem?.QueueFree();
		_neutralTeamItem = null;

		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		if (_queueResort)
		{
			_queueResort = false;
			SortList();
		}
		base._Process(delta);
	}

	private void OnStatDefinitionChanged(Stat stat)
	{
		CreateHeader();
		Refresh();
	}

	private void TeamChanged(Team _)
	{
		Refresh();
	}

	public void Refresh()
	{
		foreach (var player in _playerToItem.Keys)
		{
			player.StatChanged.Disconnect(OnPlayerStatChanged);
			player.TeamChanged.Disconnect(OnPlayerTeamChanged);
		}

		var savedTeamCollapse = new Dictionary<Team, bool>();
		foreach (var kvp in _teamToItem)
		{
			savedTeamCollapse[kvp.Key] = kvp.Value.IsCollapsed;
		}
		bool savedNeutralCollapse = _neutralTeamItem?.IsCollapsed ?? false;

		foreach (var item in _playerToItem)
		{
			item.Value.QueueFree();
		}
		foreach (var item in _teamToItem)
		{
			item.Value.QueueFree();
		}
		_neutralTeamItem?.QueueFree();
		_playerToItem.Clear();
		_teamToItem.Clear();
		_neutralTeamItem = null;

		foreach (Player plr in _players.GetPlayers())
		{
			AddPlayer(plr);
		}

		if (_players.LocalPlayer != null && !_playerToItem.ContainsKey(_players.LocalPlayer))
		{
			AddPlayer(_players.LocalPlayer);
		}

		foreach (Team team in Teams.GetTeams())
		{
			AddTeam(team);
		}

		CreateNeutralTeam();

		SortList();

		foreach (var kvp in _teamToItem)
		{
			if (savedTeamCollapse.TryGetValue(kvp.Key, out bool collapsed) && collapsed)
			{
				kvp.Value.SetCollapsed(true);
			}
		}
		if (savedNeutralCollapse && _neutralTeamItem != null)
		{
			_neutralTeamItem.SetCollapsed(true);
		}

		ApplyTeamVisibility();
		LeaderboardUpdate();
	}

	private void AddPlayer(Player player)
	{
		if (_playerToItem.ContainsKey(player))
			return;

		UILeaderboardUserItem card = Globals.CreateInstanceFromScene<UILeaderboardUserItem>(ItemPath);
		card.TargetPlayer = player;
		card.Leaderboard = this;

		_layout.AddChild(card);

		foreach (var st in Stats.GetStats())
			card.AddStat(st);

		player.StatChanged.Connect(OnPlayerStatChanged);
		player.TeamChanged.Connect(OnPlayerTeamChanged);
		_playerToItem[player] = card;

		ApplyTeamVisibility();
		LeaderboardUpdate();
	}

	private void AddTeam(Team team)
	{
		UILeaderboardTeamItem card = Globals.CreateInstanceFromScene<UILeaderboardTeamItem>(TeamItemPath);
		card.TargetTeam = team;
		card.Leaderboard = this;

		_layout.AddChild(card);

		foreach (var st in Stats.GetStats())
		{
			card.AddStat(st);
		}

		_teamToItem[team] = card;
		LeaderboardUpdate();
	}

	private void RemovePlayer(Player player)
	{
		if (!_playerToItem.TryGetValue(player, out UILeaderboardUserItem? card))
			return;

		player.StatChanged.Disconnect(OnPlayerStatChanged);
		player.TeamChanged.Disconnect(OnPlayerTeamChanged);
		card.QueueFree();
		_playerToItem.Remove(player);

		CallDeferred(nameof(RefreshAfterRemove));
	}

	private void RefreshAfterRemove()
	{
		if (_neutralTeamItem != null && !_playerToItem.Keys.Any(p => p.Team == null))
		{
			_neutralTeamItem.QueueFree();
			_neutralTeamItem = null;
		}

		foreach (var st in Stats.GetStats())
		{
			if (st is Stat stat)
			{
				foreach (var item in _teamToItem.Values)
					item.UpdateStat(stat);
				_neutralTeamItem?.UpdateStat(stat);
			}
		}

		ApplyTeamVisibility();
		LeaderboardUpdate();
	}

	public void SortList()
	{
		Stat? stat = Stats.FindChildByIndex(0) as Stat;

		var allItems = new List<(int TeamIndex, double? Value, int ItemType, Node Item)>();

		foreach (var kvp in _teamToItem)
		{
			var teamIndex = kvp.Key.Index;
			var value = stat?.GetTotalForTeam(kvp.Key);
			allItems.Add((teamIndex, value, 0, kvp.Value));
		}

		foreach (var kvp in _playerToItem)
		{
			var teamIndex = kvp.Key.Team?.Index ?? int.MaxValue;
			var value = stat?.Get(kvp.Key) as double?;
			allItems.Add((teamIndex, value, 1, kvp.Value));
		}

		if (_neutralTeamItem != null)
		{
			allItems.Add((int.MaxValue, null, 0, _neutralTeamItem));
		}

		var sortedItems = allItems.OrderBy(x => x.TeamIndex).ThenBy(x => x.ItemType);

		if (stat != null)
		{
			sortedItems = sortedItems
				.ThenByDescending(x => x.Value.HasValue)
				.ThenByDescending(x => x.Value ?? double.MinValue);
		}

		var itemsList = sortedItems.ToList();

		for (int i = 0; i < itemsList.Count; i++)
		{
			itemsList[i].Item.GetParent()?.MoveChild(itemsList[i].Item, i + 1);
		}
	}

	public void QueueSortList()
	{
		_queueResort = true;
	}

	private void ApplyTeamVisibility()
	{
		foreach (var kvp in _playerToItem)
		{
			bool visible = true;
			if (kvp.Key.Team != null && _teamToItem.TryGetValue(kvp.Key.Team, out var teamItem))
				visible = !teamItem.IsCollapsed;
			else if (kvp.Key.Team == null && _neutralTeamItem != null)
				visible = !_neutralTeamItem.IsCollapsed;
			kvp.Value.Visible = visible;
		}
	}

	public void OnTeamCollapseToggled(UILeaderboardTeamItem teamItem)
	{
		ApplyTeamVisibility();
		LeaderboardUpdate();
	}

	private void CreateNeutralTeam()
	{
		if (Teams.GetTeams().Length == 0)
			return;

		Player[] neutralPlayers = _playerToItem.Keys.Where(p => p.Team == null).ToArray();
		if (neutralPlayers.Length == 0)
			return;

		UILeaderboardTeamItem card = Globals.CreateInstanceFromScene<UILeaderboardTeamItem>(TeamItemPath);
		card.Leaderboard = this;
		card.SetNeutral("Neutral Team", new Color(0.56f, 0.61f, 0.66f));
		_layout.AddChild(card);
		_neutralTeamItem = card;
		foreach (var st in Stats.GetStats())
			card.AddStat(st);
	}

	internal Player[] GetNeutralPlayers()
	{
		return _playerToItem.Keys.Where(p => p.Team == null).ToArray();
	}

	internal double GetNeutralStatTotal(Stat stat)
	{
		double total = 0;
		foreach (var kvp in _playerToItem)
		{
			if (kvp.Key.Team == null && stat.Get(kvp.Key) is double d)
				total += d;
		}
		return total;
	}

	private void OnPlayerTeamChanged(Team? team)
	{
		if (!IsInsideTree())
			return;

		if (team == null && _neutralTeamItem == null)
			CreateNeutralTeam();
		else if (team != null && _neutralTeamItem != null && !_playerToItem.Keys.Any(p => p.Team == null))
		{
			_neutralTeamItem.QueueFree();
			_neutralTeamItem = null;
		}

		foreach (var st in Stats.GetStats())
		{
			if (st is Stat stat)
			{
				foreach (var item in _teamToItem.Values)
					item.UpdateStat(stat);
				_neutralTeamItem?.UpdateStat(stat);
			}
		}

		QueueSortList();
		ApplyTeamVisibility();
	}

	private void OnPlayerStatChanged(Stat stat, object? _)
	{
		foreach (var item in _teamToItem.Values)
			item.UpdateStat(stat);
		_neutralTeamItem?.UpdateStat(stat);
	}

	private void CreateHeader()
	{
		var statsBox = _headerRow.FindChild("Stats") as HBoxContainer;
		if (statsBox == null)
			return;

		foreach (Node node in statsBox.GetChildren())
		{
			node.QueueFree();
		}

		foreach (var stat in Stats.GetStats())
		{
			if (stat.IsDeleted) continue;
			Label headerLabel = new()
			{
				Text = stat.GetDisplayName(),
				CustomMinimumSize = new(70, 0),
				HorizontalAlignment = HorizontalAlignment.Center,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			headerLabel.AddThemeFontSizeOverride("font_size", 13);
			statsBox.AddChild(headerLabel);
		}
	}

	private float GetMaxHeight()
	{
		if (!Globals.IsMobileBuild)
		{
			return LeaderboardMaxHeight;
		}

		float viewportHeight = GetViewportRect().Size.Y;
		const float reservedBottom = 240f;
		return Mathf.Clamp(viewportHeight - reservedBottom - GlobalPosition.Y, 120f, LeaderboardMaxHeight);
	}

	private async void LeaderboardUpdate()
	{
		_container.Visible = Visible && _playerToItem.Count > 0;

		if (!Visible || _playerToItem.Count == 0)
			return;

		// Resize based on container, need to be resized on next frame
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		float ys = _layout.Size.Y + 16;

		if (ys > GetMaxHeight())
		{
			ys = GetMaxHeight();
		}

		_container.Size = new(_userCard.Size.X, ys);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		_container.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize);
	}
}
