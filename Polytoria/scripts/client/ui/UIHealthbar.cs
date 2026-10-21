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
	private Color _staminaFullColor;
	private Color _staminaOutColor;

	public override void _EnterTree()
	{
		_healthFullColor = Color.FromHtml("#4fe883");
		_healthOutColor = Color.FromHtml("#DD5555");
		_staminaFullColor = Color.FromHtml("#0097ff");
		_staminaOutColor = Color.FromHtml("#026fbdff");
		base._EnterTree();
	}

	public override void _Process(double delta)
	{
		if (CoreUI.Root.Players.LocalPlayer != null)
		{
			Player localplayer = CoreUI.Root.Players.LocalPlayer;
			float health = localplayer.Health;
			float maxHealth = localplayer.MaxHealth;
			Color healthClr = _healthOutColor.Lerp(_healthFullColor, Mathf.Clamp(health / maxHealth, 0, 1));

			_heart.Modulate = healthClr;
			_healthBar.Modulate = healthClr;

			_staminaBar.Visible = localplayer.UseStamina;
			_staminaBar.Value = localplayer.Stamina;
			_staminaBar.MaxValue = localplayer.MaxStamina;
			_staminaBar.SelfModulate = localplayer.IsExhausted ? _staminaOutColor : _staminaFullColor;

			_healthBar.Value = health;
			_healthBar.MaxValue = maxHealth;

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

			if (health <= -100)
			{
				_healthLabel.Text = "D:";
			}
			else
			{
				_healthLabel.Text = Mathf.Round(health).ToString();
			}
		}
	}
}
