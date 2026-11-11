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

	private ShaderMaterial? _healthBarMat;
	private float _healthBarValue = -1;

	public override void _EnterTree()
	{
		_healthBarMat ??= (ShaderMaterial)_healthBar.GetActiveMaterial(0).Duplicate();
		_healthBar.MaterialOverride = _healthBarMat;
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
		bool showHealthBar = false;
		if (Target.Vitals is Vitals v)
		{
			showHealthBar = v.Health < v.MaxHealth;
			_healthBar.Visible = showHealthBar;
			if (showHealthBar)
			{
				float health = v.MaxHealth > 0 ? v.Health / v.MaxHealth : 1;
				if (health != _healthBarValue)
				{
					_healthBarValue = health;
					_healthBarMat!.SetShaderParameter(_valueParam, health);
				}
			}
		}
		else
		{
			_healthBar.Visible = false;
		}
		_titleLabel.Offset = _outlineLabel.Offset = new(0, showHealthBar ? 50 : 16);
	}
}
