// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public static class CardAnimator
{
	public const float DefaultPressedScale = 0.94f;

	private const float StaggerDelay = 0.045f;
	private const float PopDuration = 0.35f;

	public static Tween PopIn(Control card, int index)
	{
		card.Modulate = new(1, 1, 1, 0);
		card.Scale = new(0.9f, 0.9f);

		float delay = index * StaggerDelay;
		card.CreateTween().TweenProperty(card, "modulate:a", 1f, PopDuration * 0.6f).SetDelay(delay);

		Tween scale = card.CreateTween();
		scale.TweenProperty(card, "scale", Vector2.One, PopDuration)
			.SetDelay(delay)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		return scale;
	}

	public static Tween Press(Control target, Tween? current, bool pressed, float pressedScale = DefaultPressedScale)
	{
		current?.Kill();

		float to = pressed ? pressedScale : 1f;
		Tween tween = target.CreateTween();
		tween.TweenProperty(target, "scale", new Vector2(to, to), pressed ? 0.1f : 0.25f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		return tween;
	}
}
