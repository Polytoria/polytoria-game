// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class Vector2Property : HBoxContainer, IProperty<Vector2>
{
	private SpinBox _x = null!;
	private SpinBox _y = null!;

	private Vector2 _value;

	public Vector2 Value
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
		if (value == null) return;
		Value = (Vector2)value;
	}

	public void Refresh()
	{
		Vector2 value = Value;
		_x.SetValueNoSignal(value.X);
		_y.SetValueNoSignal(value.Y);
	}

	public override void _Ready()
	{
		_x = GetNode<SpinBox>("X");
		_y = GetNode<SpinBox>("Y");

		ConnectAxis(_x, axisIndex: 0);
		ConnectAxis(_y, axisIndex: 1);

		Refresh();
	}

	private void ConnectAxis(SpinBox spinBox, int axisIndex)
	{
		spinBox.ValueChanged += value =>
		{
			Vector2 current = Value;
			Vector2 newValue = axisIndex switch
			{
				0 => new Vector2((float)value, current.Y),
				1 => new Vector2(current.X, (float)value),
				_ => current
			};

			ValueChanged?.Invoke(newValue);
		};
	}
}
