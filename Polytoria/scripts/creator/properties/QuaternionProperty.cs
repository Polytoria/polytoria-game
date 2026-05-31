// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class QuaternionProperty : HBoxContainer, IProperty<Quaternion>
{

	private SpinBox _x = null!;
	private SpinBox _y = null!;
	private SpinBox _z = null!;
	private SpinBox _w = null!;

	private Quaternion _value;

	public Quaternion Value
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
		Value = (Quaternion)value;
	}

	public void Refresh()
	{
		Quaternion value = Value;
		_x.SetValueNoSignal(value.X);
		_y.SetValueNoSignal(value.Y);
		_z.SetValueNoSignal(value.Z);
		_w.SetValueNoSignal(value.W);
	}

	public override void _Ready()
	{
		_x = GetNode<SpinBox>("X");
		_y = GetNode<SpinBox>("Y");
		_z = GetNode<SpinBox>("Z");
		_w = GetNode<SpinBox>("W");

		ConnectAxis(_x, axisIndex: 0);
		ConnectAxis(_y, axisIndex: 1);
		ConnectAxis(_z, axisIndex: 2);
		ConnectAxis(_w, axisIndex: 3);

		Refresh();
	}

	private void ConnectAxis(SpinBox spinBox, int axisIndex)
	{
		spinBox.ValueChanged += value =>
		{
			Quaternion current = Value;
			Quaternion newValue = axisIndex switch
			{
				0 => new Quaternion((float)value, current.Y, current.Z, current.W),
				1 => new Quaternion(current.X, (float)value, current.Z, current.W),
				2 => new Quaternion(current.X, current.Y, (float)value, current.W),
				3 => new Quaternion(current.X, current.Y, current.Z, (float)value),
				_ => current
			};

			ValueChanged?.Invoke(newValue);
		};
	}
}
