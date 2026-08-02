// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class UIScaleProperty : HBoxContainer, IProperty<UIScale>
{
	private SpinBox _offset = null!;
	private SpinBox _scale = null!;

	private UIScale _value;

	public UIScale Value
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

	public object? GetValue() => Value;

	public void SetValue(object? value)
	{
		if (value is UIScale scale)
			Value = scale;
	}

	public void Refresh()
	{
		UIScale value = Value;
		_offset.SetValueNoSignal(value.Offset);
		_scale.SetValueNoSignal(value.Scale * 100f);
	}

	public override void _Ready()
	{
		_offset = GetNode<SpinBox>("Offset");
		_scale = GetNode<SpinBox>("Scale");

		_offset.ValueChanged += v =>
		{
			_value.Offset = (float)v;
			ValueChanged?.Invoke(_value);
		};

		_scale.ValueChanged += v =>
		{
			_value.Scale = (float)v / 100f;
			ValueChanged?.Invoke(_value);
		};

		Refresh();
	}
}
