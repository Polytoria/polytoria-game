// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI;

public partial class UIHealthbar : Control
{
	[Export] private ProgressBar _healthBar = null!;
	[Export] private ProgressBar _staminaBar = null!;
	[Export] private Label _healthLabel = null!;
	[Export] private TextureRect _heart = null!;
	[Export] private AnimationPlayer _staminaBarAnim = null!;
	public CoreUIRoot CoreUI = null!;

	private bool _staminaBarAppeared = false;

	private Color _healthFullColor;
	private Color _healthOutColor;

	public override void _EnterTree()
	{
		_healthFullColor = Color.FromHtml("#4fe883");
		_healthOutColor = Color.FromHtml("#DD5555");
		base._EnterTree();
	}

	public override void _Process(double delta)
	{
		if (CoreUI.Root.Players.LocalPlayer is Player localplayer)
		{
			if (localplayer.Vitals is Vitals v)
			{
				_heart.Visible = _healthBar.Visible = true;
				float health = v.Health;
				float maxHealth = v.MaxHealth;
				Color healthClr = _healthOutColor.Lerp(_healthFullColor, Mathf.Clamp(health / maxHealth, 0, 1));

				_heart.Modulate = healthClr;
				_healthBar.Modulate = healthClr;

				_healthBar.Value = health;
				_healthBar.MaxValue = maxHealth;

				_healthLabel.Text = (health <= -maxHealth) ? "D:" : _healthLabel.Text = Mathf.Round(health).ToString();
			}
			else
			{
				_heart.Visible = _healthBar.Visible = false;
			}

			_staminaBar.Visible = localplayer.UseStamina;
			_staminaBar.Value = localplayer.Stamina;
			_staminaBar.MaxValue = localplayer.MaxStamina;

			// Hide/Show the stamina bar
			if (localplayer.Stamina == localplayer.MaxStamina || !localplayer.UseStamina)
			{
				if (_staminaBarAppeared)
				{
					_staminaBarAppeared = false;
					_staminaBarAnim.Play("disappear");
				}
			}
			else
			{
				if (!_staminaBarAppeared)
				{
					_staminaBarAppeared = true;
					_staminaBarAnim.Play("appear");
				}
			}
		}
	}
}
