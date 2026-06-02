// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using Polytoria.Enums;
using System;
using ColorPicker = Polytoria.Creator.UI.ColorPicker;

namespace Polytoria.Creator.Properties;

public sealed partial class ShadowLayerProperty : MarginContainer, IProperty<ShadowLayer>
{
	private Button _colorBtn = null!;
	private StyleBoxFlat _colorPreview = null!;
	private SpinBox _offsetX = null!;
	private SpinBox _offsetY = null!;
	private SpinBox _radius = null!;
	private SpinBox _spread = null!;
	private OptionButton _blendMode = null!;

	private ShadowLayer _value;

	public ShadowLayer Value
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
		if (value is ShadowLayer layer)
			Value = layer;
	}

	public void Refresh()
	{
		if (_colorPreview == null) return;
		_colorPreview.BgColor = _value.Color;
		_offsetX.SetValueNoSignal(_value.Offset.X);
		_offsetY.SetValueNoSignal(_value.Offset.Y);
		_radius.SetValueNoSignal(_value.Radius);
		_spread.SetValueNoSignal(_value.Spread);
		_blendMode.Select((int)_value.BlendMode);
	}

	public override void _Ready()
	{
		_colorBtn = GetNode<Button>("VBox/Row1/Color");
		var previewPanel = _colorBtn.GetNode<Panel>("Preview");
		_colorPreview = new StyleBoxFlat();
		previewPanel.AddThemeStyleboxOverride("panel", _colorPreview);
		_offsetX = GetNode<SpinBox>("VBox/Row2/OffsetX");
		_offsetY = GetNode<SpinBox>("VBox/Row2/OffsetY");
		_radius = GetNode<SpinBox>("VBox/Row3/Radius");
		_spread = GetNode<SpinBox>("VBox/Row3/Spread");
		_blendMode = GetNode<OptionButton>("VBox/Row1/BlendMode");

		WireEvents();
		SetNotifyTransform(true);
		Refresh();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationTransformChanged)
			ColorPicker.Singleton.CalculatePosition(_colorBtn);
	}

	private void WireEvents()
	{
		_colorBtn.Pressed += () =>
		{
			ColorPicker.Singleton.SwitchTo(_colorBtn, _value.Color,
				previewColor =>
				{
					_value = new ShadowLayer
					{
						Color = previewColor,
						Offset = _value.Offset,
						Radius = _value.Radius,
						Spread = _value.Spread,
						BlendMode = _value.BlendMode,
					};
					_colorPreview.BgColor = previewColor;
				},
				() =>
				{
					EmitChanged();
				});
		};

		_offsetX.ValueChanged += v =>
		{
			_value.Offset = new Vector2((float)v, _value.Offset.Y);
			EmitChanged();
		};

		_offsetY.ValueChanged += v =>
		{
			_value.Offset = new Vector2(_value.Offset.X, (float)v);
			EmitChanged();
		};

		_radius.ValueChanged += v =>
		{
			_value.Radius = (float)v;
			EmitChanged();
		};

		_spread.ValueChanged += v =>
		{
			_value.Spread = (float)v;
			EmitChanged();
		};

		_blendMode.ItemSelected += idx =>
		{
			_value.BlendMode = (BlendModeEnum)idx;
			EmitChanged();
		};
	}

	private void EmitChanged() => ValueChanged?.Invoke(_value);
}
