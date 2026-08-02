// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class Vector3Property : HBoxContainer, IProperty<Vector3>
{

	private SpinBox _x = null!;
	private SpinBox _y = null!;
	private SpinBox _z = null!;

	private Vector3 _value;

	public Vector3 Value
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
		Value = (Vector3)value;
	}

	public void Refresh()
	{
		Vector3 value = Value;
		_x.SetValueNoSignal(value.X);
		_y.SetValueNoSignal(value.Y);
		_z.SetValueNoSignal(value.Z);
	}

	public override void _Ready()
	{
		_x = GetNode<SpinBox>("X");
		_y = GetNode<SpinBox>("Y");
		_z = GetNode<SpinBox>("Z");

		ConnectAxis(_x, axisIndex: 0);
		ConnectAxis(_y, axisIndex: 1);
		ConnectAxis(_z, axisIndex: 2);

		Refresh();
	}

	private void ConnectAxis(SpinBox spinBox, int axisIndex)
	{
		spinBox.ValueChanged += value =>
		{
			Vector3 current = Value;
			Vector3 newValue = axisIndex switch
			{
				0 => new Vector3((float)value, current.Y, current.Z),
				1 => new Vector3(current.X, (float)value, current.Z),
				2 => new Vector3(current.X, current.Y, (float)value),
				_ => current
			};

			ValueChanged?.Invoke(newValue);
		};
	}
}
