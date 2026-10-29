// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client;

public partial class Nametag : Node3D
{
	private static readonly StringName _valueParam = "value";

	[Export] private Label3D _titleLabel = null!;
	[Export] private Label3D _outlineLabel = null!;
	[Export] private MeshInstance3D _healthBar = null!;

	public NPC Target = null!;

	public override void _EnterTree()
	{
		Target.Renamed.Connect(UpdateNameTag);
		UpdateNameTag();
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		Target.Renamed.Disconnect(UpdateNameTag);
		base._ExitTree();
	}

	public void UpdateNameTag()
	{
		Visible = Target.UseNametag && Target.NametagVisibleRadius > 0 && !(Target is Player plr && plr.IsLocal);
		_titleLabel.Text = _outlineLabel.Text = Target.DisplayName != string.Empty ? Target.DisplayName : Target.Name;
		_titleLabel.VisibilityRangeEnd = _outlineLabel.VisibilityRangeEnd = _healthBar.VisibilityRangeEnd = Target.NametagVisibleRadius;
		_healthBar.Visible = Target.Health < Target.MaxHealth;
		_healthBar.SetInstanceShaderParameter(_valueParam, Target.MaxHealth > 0 ? Target.Health / Target.MaxHealth : 1);
		_titleLabel.Offset = _outlineLabel.Offset = new(0, _healthBar.Visible ? 50 : 16);
	}
}
