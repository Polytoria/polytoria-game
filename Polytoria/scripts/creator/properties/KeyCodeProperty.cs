// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.UI.Prompt;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class KeyCodeProperty : Button, IProperty<KeyCodeEnum>
{
	private KeyCodeEnum _value;
	private bool _listening;

	public KeyCodeEnum Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		if (value is KeyCodeEnum key)
		{
			Value = key;
		}
	}

	public void Refresh()
	{
		Text = _listening ? "Press a key..." : UIInteractionPrompt.GetKeyLabel(Value);
	}

	public override void _Ready()
	{
		Pressed += OnPressed;
		Refresh();
	}

	private void OnPressed()
	{
		_listening = true;
		Refresh();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_listening || !@event.IsPressed() || @event.IsEcho()) return;
		if (@event is not (InputEventKey or InputEventMouseButton)) return;

		GetViewport().SetInputAsHandled();
		_listening = false;

		bool cancel = @event is InputEventKey { Keycode: Key.Escape }
			|| @event is InputEventMouseButton && !GetGlobalRect().HasPoint(GetGlobalMousePosition());

		if (!cancel && InputService.InputEventToKeyCode(@event) is KeyCodeEnum key)
		{
			Value = key;
			ValueChanged?.Invoke(key);
		}
		Refresh();
	}
}
