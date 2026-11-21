// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class NavbarButton : Button
{
	private const float InactiveAlpha = 0.5f;

	[Export]
	public MobileViewEnum SwitchTo;

	[Export] private Control _icon = null!;

	private Tween? _tween;

	public override void _Ready()
	{
		Modulate = new(1, 1, 1, InactiveAlpha);
		base._Ready();
	}

	public void SetActive(bool active)
	{
		_tween?.Kill();
		_tween = CreateTween().SetParallel();
		_tween.TweenProperty(this, "modulate:a", active ? 1f : InactiveAlpha, 0.2f);

		if (active)
		{
			_icon.Scale = new(0.75f, 0.75f);
			_tween.TweenProperty(_icon, "scale", Vector2.One, 0.4f)
				.SetEase(Tween.EaseType.Out)
				.SetTrans(Tween.TransitionType.Back);
		}
	}

	public override void _Pressed()
	{
		MobileUI.Singleton.SwitchTo(SwitchTo);
		base._Pressed();
	}
}
