// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class ShadowLayerArrayProperty : VBoxContainer, IProperty<ShadowLayer[]>
{
	private VBoxContainer _layersContainer = null!;
	private Button _addButton = null!;

	private ShadowLayer[] _value = [new ShadowLayer()];

	public ShadowLayer[] Value
	{
		get => _value;
		set
		{
			_value = value.Length > 0 ? value : [new ShadowLayer()];
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue() => Value;

	public void SetValue(object? value)
	{
		if (value is ShadowLayer[] layers)
			Value = layers;
	}

	public void Refresh()
	{
		foreach (Node child in _layersContainer.GetChildren())
			child.QueueFree();

		for (int i = 0; i < _value.Length; i++)
		{
			_layersContainer.AddChild(CreateLayerRow(i));
		}
	}

	public override void _Ready()
	{
		_layersContainer = GetNode<VBoxContainer>("Layers");
		_addButton = GetNode<Button>("AddButton");

		_addButton.Pressed += () =>
		{
			Array.Resize(ref _value, _value.Length + 1);
			_value[^1] = new ShadowLayer();
			_layersContainer.AddChild(CreateLayerRow(_value.Length - 1));
			EmitChanged();
		};

		Refresh();
	}

	private Node CreateLayerRow(int index)
	{
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 2);

		ShadowLayerProperty editor = (ShadowLayerProperty)Globals.LoadProperty(typeof(ShadowLayer));
		editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		editor.Value = _value[index];
		editor.ValueChanged += updated =>
		{
			if (updated is ShadowLayer sl && index < _value.Length)
			{
				_value[index] = sl;
				EmitChanged();
			}
		};
		row.AddChild(editor);

		Button removeBtn = new()
		{
			CustomMinimumSize = new Vector2(28, 28),
			Text = "X",
			ThemeTypeVariation = "FlatButton",
		};
		removeBtn.Pressed += () =>
		{
			int currentIndex = _layersContainer.GetChildren().IndexOf(row);
			if (currentIndex >= 0 && currentIndex < _value.Length)
				RemoveLayerAt(currentIndex);
		};
		row.AddChild(removeBtn);

		return row;
	}

	private void RemoveLayerAt(int index)
	{
		if (_value.Length <= 1 || index < 0 || index >= _value.Length) return;

		var newValue = new ShadowLayer[_value.Length - 1];
		if (index > 0)
			Array.Copy(_value, 0, newValue, 0, index);
		if (index < _value.Length - 1)
			Array.Copy(_value, index + 1, newValue, index, _value.Length - index - 1);
		_value = newValue;

		if (index < _layersContainer.GetChildCount())
		{
			Node row = _layersContainer.GetChild(index);
			_layersContainer.RemoveChild(row);
			row.QueueFree();
		}

		EmitChanged();
	}

	private void EmitChanged() => ValueChanged?.Invoke(_value);
}
