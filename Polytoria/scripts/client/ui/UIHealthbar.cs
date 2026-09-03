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
	[Export] private AnimationPlayer _healthBarAnim = null!;
	public CoreUIRoot CoreUI = null!;

	private bool _staminaBarAppeared = false;
	private bool _healthBarAppeared = false;

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
		Player? localplayer = CoreUI.Root.Players.LocalPlayer;
		if (localplayer == null) return;
		CharacterModel? localcharacter = localplayer.Character;
		if (localcharacter == null)
		{
			if (_healthBarAppeared)
			{
				_healthBar.Value = 0;
				_healthLabel.Text = "D:";
				_heart.Modulate = _healthOutColor;
				_healthBar.Modulate = _healthOutColor;
				_healthBarAppeared = false;
				_healthBarAnim.Play("disappear");
			}

			// Hide/Show the stamina bar
			if (_staminaBarAppeared)
			{
				_staminaBarAppeared = false;
				_staminaBarAnim.Play("disappear");
			}
		}
		else
		{
			float health = localcharacter.Health;
			float maxHealth = localcharacter.MaxHealth;
			Color healthClr = _healthOutColor.Lerp(_healthFullColor, Mathf.Clamp(health / maxHealth, 0, 1));

			_heart.Modulate = healthClr;
			_healthBar.Modulate = healthClr;

			_staminaBar.Visible = localcharacter.UseStamina;
			_staminaBar.Value = localcharacter.Stamina;
			_staminaBar.MaxValue = localcharacter.MaxStamina;

			_healthBar.Value = health;
			_healthBar.MaxValue = maxHealth;

			if (!_healthBarAppeared)
			{
				_healthBarAppeared = true;
				_healthBarAnim.Play("appear");
			}

			// Hide/Show the stamina bar
			if (localcharacter.Stamina == localcharacter.MaxStamina || !localcharacter.UseStamina)
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
