// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class MobileNavbar : Control
{
	[Export] private Container _buttonContainer = null!;
	[Export] private Control _indicator = null!;

	private NavbarButton? _activeButton;
	private Tween? _indicatorTween;

	public override void _Ready()
	{
		MobileUI.Singleton.ViewPathSwitched += OnViewPathSwitched;
		_buttonContainer.SortChildren += OnButtonsSorted;
		_indicator.Visible = false;
	}

	public override void _ExitTree()
	{
		MobileUI.Singleton.ViewPathSwitched -= OnViewPathSwitched;
		base._ExitTree();
	}

	private void OnViewPathSwitched(MobileViewEnum view)
	{
		NavbarButton? target = null;
		foreach (Node child in _buttonContainer.GetChildren())
		{
			if (child is NavbarButton button && button.SwitchTo == view)
			{
				target = button;
				break;
			}
		}

		if (target == null || target == _activeButton)
		{
			return;
		}

		foreach (Node child in _buttonContainer.GetChildren())
		{
			if (child is NavbarButton button)
			{
				button.SetActive(button == target);
			}
		}

		bool animate = _activeButton != null;
		_activeButton = target;
		MoveIndicator(animate);
	}

	private void OnButtonsSorted()
	{
		MoveIndicator(false);
	}

	private void MoveIndicator(bool animate)
	{
		if (_activeButton == null)
		{
			return;
		}

		float x = _buttonContainer.Position.X + _activeButton.Position.X;
		float width = _activeButton.Size.X;

		_indicatorTween?.Kill();
		_indicator.Visible = true;

		if (!animate)
		{
			_indicator.Position = new(x, _indicator.Position.Y);
			_indicator.Size = new(width, _indicator.Size.Y);
			return;
		}

		_indicatorTween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quint);
		_indicatorTween.TweenProperty(_indicator, "position:x", x, 0.35f);
		_indicatorTween.TweenProperty(_indicator, "size:x", width, 0.35f);
	}
}
