// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class GamepadKeyCodeProperty : OptionButton, IProperty<KeyCodeEnum>
{
	private KeyCodeEnum _value;

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
		Selected = GetItemIndex((int)Value);
	}

	public override void _Ready()
	{
		AddItem("None", (int)KeyCodeEnum.None);
		foreach (KeyCodeEnum key in Enum.GetValues<KeyCodeEnum>())
		{
			bool isStick = key is >= KeyCodeEnum.GamepadAxisLeftX and <= KeyCodeEnum.GamepadAxisRightY;
			if (InputService.IsGamepadKey(key) && !isStick)
			{
				AddItem(key.ToString().Replace("Gamepad", ""), (int)key);
			}
		}

		Refresh();

		GetPopup().IdPressed += id =>
		{
			ValueChanged?.Invoke((KeyCodeEnum)(int)id);
		};
	}
}
