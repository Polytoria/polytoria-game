// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Playerlist.Stats;

public partial class UIUserCardStat : Node
{
	[Export] private Label _nameLabel = null!;
	[Export] private Label _valueLabel = null!;

	public UIUserCard Root = null!;
	public Stat TargetStat = null!;

	public override void _Ready()
	{
		_nameLabel.Text = TargetStat.GetDisplayName();
		_valueLabel.Text = TargetStat.GetDisplayValue(UIUserCard.TargetPlayer);
		UIUserCard.TargetPlayer.StatChanged.Connect(OnPlayerStatChanged);
	}

	private void OnPlayerStatChanged(Stat k, object? _)
	{
		if (k != TargetStat) return;
		_valueLabel.Text = TargetStat.GetDisplayValue(UIUserCard.TargetPlayer);
	}
}
