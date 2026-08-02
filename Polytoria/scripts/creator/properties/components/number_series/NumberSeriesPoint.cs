// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties.Components;

public partial class NumberSeriesPoint : Control
{
	[Export] private SpinBox _percentageBox = null!;
	[Export] private SpinBox _valueBox = null!;
	[Export] private Button _deleteButton = null!;

	public event Action<float>? OffsetChanged;
	public event Action<float>? ValueChanged;
	public event Action? DeleteRequested;

	public float OffsetValue;
	public float Value;
	public override void _Ready()
	{
		_percentageBox.ValueChanged += val =>
		{
			OffsetChanged?.Invoke(Mathf.Clamp((float)val / 100, 0, 1));
		};

		_valueBox.ValueChanged += val =>
		{
			ValueChanged?.Invoke((float)val);
		};

		_deleteButton.Pressed += () => DeleteRequested?.Invoke();
		_percentageBox.SetValueNoSignal(OffsetValue * 100);
		_valueBox.SetValueNoSignal(Value);
	}
}
