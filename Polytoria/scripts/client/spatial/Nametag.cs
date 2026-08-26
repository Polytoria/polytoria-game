// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;

namespace Polytoria.Client;

public partial class Nametag : Node3D
{
	private Label _titleLabel = null!;
	private ProgressBar _healthBar = null!;
	private Node3D _nametag = null!;
	private string? _lastTitle;
	private float _lastHealth = float.NaN;
	private float _lastMaxHealth = float.NaN;

	public NPC Target = null!;

	public override void _Ready()
	{
		_nametag = Globals.CreateInstanceFromScene<Node3D>("res://scenes/client/spatial/nametag.tscn");
		AddChild(_nametag);
		_titleLabel = _nametag.GetNode<Label>("SubViewport/Control/Title");
		_healthBar = _nametag.GetNode<ProgressBar>("SubViewport/Control/Healthbar");
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		UpdateNameTag();
	}

	public void UpdateNameTag()
	{
		bool useNametag = Target.UseNametag;

		Camera? cam = Target.Root.Environment.CurrentCamera;

		// Check distance from camera if is with-in radius
		if (cam != null && useNametag)
		{
			float radius = Target.NametagVisibleRadius;
			useNametag = (cam.Position - GlobalPosition).LengthSquared() < radius * radius;
		}

		// Hide if self is Target
		if (Target == Target.Root.Players?.LocalPlayer)
		{
			useNametag = false;
		}

		if (Visible != useNametag)
		{
			Visible = useNametag;
		}

		if (!useNametag)
		{
			return;
		}

		string title = Target.DisplayName != string.Empty ? Target.DisplayName : Target.Name;
		if (_lastTitle != title)
		{
			_lastTitle = title;
			_titleLabel.Text = title;
		}

		float health = Target.Health;
		float maxHealth = Target.MaxHealth;
		if (_lastHealth != health || _lastMaxHealth != maxHealth)
		{
			_lastHealth = health;
			_lastMaxHealth = maxHealth;
			_healthBar.Visible = health < maxHealth;
			_healthBar.MaxValue = maxHealth;
			_healthBar.Value = health;
		}
	}
}
