// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Playerlist;

public partial class UILeaderboardTeamItem : Control
{
	private readonly Dictionary<Stat, Label> _statToLabel = [];

	[Export] private Label _teamNameLabel = null!;
	[Export] private Control _statsBox = null!;

	public Team TargetTeam = null!;
	public UILeaderboard Leaderboard = null!;
	public bool IsCollapsed { get; private set; } = false;

	private bool _isNeutral;
	private Color _neutralColor;

	public void SetNeutral(string name, Color color)
	{
		_isNeutral = true;
		_neutralColor = color;
		_teamNameLabel.Text = name;
	}

	public override void _Ready()
	{
		if (!_isNeutral)
		{
			_teamNameLabel.Text = TargetTeam.GetDisplayName();
			ApplyColor();
			TargetTeam.PropertyChanged.Connect(OnPropertyChanged);
		}
		else
		{
			SelfModulate = _neutralColor;
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			ToggleCollapse();
			AcceptEvent();
		}
	}

	public void ToggleCollapse()
	{
		IsCollapsed = !IsCollapsed;
		Leaderboard?.OnTeamCollapseToggled(this);
	}

	public void SetCollapsed(bool collapsed)
	{
		IsCollapsed = collapsed;
	}

	public override void _ExitTree()
	{
		if (!_isNeutral)
			TargetTeam.PropertyChanged.Disconnect(OnPropertyChanged);
		base._ExitTree();
	}

	private void OnPropertyChanged(string _)
	{
		ApplyColor();
	}

	private void ApplyColor()
	{
		SelfModulate = TargetTeam.Color;
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
		if (Leaderboard == null)
		{
			return;
		}

		double total;
		if (_isNeutral)
			total = Leaderboard.GetNeutralStatTotal(stat);
		else
			total = stat.GetTotalForTeam(TargetTeam);
		_statToLabel[stat].Text = total.ToKMB();
		Leaderboard.QueueSortList();
	}
}
