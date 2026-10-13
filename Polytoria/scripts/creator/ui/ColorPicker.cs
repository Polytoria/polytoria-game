// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using static Godot.ColorPicker;

namespace Polytoria.Creator.UI;

public sealed partial class ColorPicker : PanelContainer
{
	public static ColorPicker Singleton { get; private set; } = null!;
	public ColorPicker()
	{
		Singleton = this;
	}

	private Godot.ColorPicker _picker = null!;
	private Button? _button;
	private ColorChangedEventHandler? _callback;
	private Action? _finishedCallback;

	public override void _Ready()
	{
		_picker = GetNode<Godot.ColorPicker>("Picker");

		GetViewport().GuiFocusChanged += focus =>
		{
			if (!Visible || focus == null)
				return;

			if (IsAncestorOf(focus) || focus == _button)
				return;

			Hide();
		};
	}

	public override void _Input(InputEvent @event)
	{
		if (!Visible || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouseButton)
			return;

		if (GetGlobalRect().HasPoint(mouseButton.Position))
			return;

		if (_button != null && _button.GetGlobalRect().HasPoint(mouseButton.Position))
			return;

		Hide();
	}

	public void SwitchTo(Button button, Color current, ColorChangedEventHandler callback, Action? finishedCallback = null)
	{
		if (button == _button)
		{
			Hide();
			return;
		}
		_finishedCallback = finishedCallback;

		Show(button, current, callback);
	}

	public void CalculatePosition(Button button)
	{
		if (button != _button)
		{
			return;
		}

		Vector2 buttonPosition = button.GlobalPosition;
		Vector2 buttonSize = button.Size;

		Vector2 pickerPosition = new(buttonPosition.X, buttonPosition.Y + buttonSize.Y + 6);
		Vector2 pickerSize = Size;

		Rect2 bounds = GetViewportRect();

		if (!bounds.HasPoint(new(pickerPosition.X + pickerSize.X, 0)))
		{
			Vector2 altPosition = new(buttonPosition.X + buttonSize.X - Size.X, pickerPosition.Y);

			if (bounds.HasPoint(altPosition))
			{
				pickerPosition = altPosition;
			}
		}

		if (!bounds.HasPoint(new(0, pickerPosition.Y + pickerSize.Y)))
		{
			Vector2 altPosition = new(pickerPosition.X, buttonPosition.Y - pickerSize.Y - 6);

			if (bounds.HasPoint(altPosition))
			{
				pickerPosition = altPosition;
			}
		}

		GlobalPosition = pickerPosition;
	}

	private void Show(Button button, Color current, ColorChangedEventHandler callback)
	{
		_button = button;

		CheckCallbackDisposed();

		if (_callback != null)
		{
			_picker.ColorChanged -= _callback;
		}

		_callback = callback;

		_picker.Color = current;
		_picker.ColorChanged += _callback;

		CalculatePosition(button);
		Visible = true;
	}

	private new void Hide()
	{
		Visible = false;

		_button = null;

		CheckCallbackDisposed();

		if (_callback != null)
		{
			_picker.ColorChanged -= _callback;
		}

		_finishedCallback?.Invoke();

		_callback = null;
	}

	private void CheckCallbackDisposed()
	{
		if (_callback?.Target is Node n && !Node.IsInstanceValid(n))
		{
			_callback = null;
		}
	}
}
