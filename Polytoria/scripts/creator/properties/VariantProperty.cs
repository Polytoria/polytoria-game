// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using Polytoria.Shared;

using static Polytoria.Scripting.Datatypes.PTVariant;

namespace Polytoria.Creator.Properties;

// The OptionButton selects the type and an inner IProperty editor is rebuilt whenever the type changes.
public sealed partial class VariantProperty : Control, IProperty<Variant>
{
	private Variant _value = ToGodot(null);
	private VariantEnum _builtType = VariantEnum.Nil;

	[Export] private OptionButton _variantEnumNode = null!;
	private PopupMenu _popup = null!;
	[Export] private Control _controlNode = null!;
	private Control? _valueNode;

	public Variant Value
	{
		get => _value;
		set
		{
			_value = value;
			Apply();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return _value;
	}

	public void SetValue(object? value)
	{
		Value = value is Variant variant ? variant : ToGodot(value);
	}

	public void Refresh()
	{
		_variantEnumNode.Selected = _variantEnumNode.GetItemIndex((int)ToScriptEnum(_value.VariantType));
	}

	// Rebuild the inner IProperty editor when the variant's type changes, then apply the current value to it.
	private void Apply()
	{
		VariantEnum type = ToScriptEnum(_value.VariantType);

		if (_valueNode == null || type != _builtType)
		{
			if (_valueNode is IProperty oldProperty)
			{
				oldProperty.ValueChanged -= OnInnerValueChanged;
			}
			_valueNode?.Free();

			_valueNode = CreateValueNode(type);
			_builtType = type;

			if (_valueNode != null)
			{
				_controlNode.AddChild(_valueNode);
				if (_valueNode is IProperty newProperty)
				{
					newProperty.ValueChanged += OnInnerValueChanged;
				}
			}
		}

		if (_valueNode is IProperty property)
		{
			object? newValue = _value.VariantType switch
			{
				Variant.Type.Bool => _value.AsBool(),
				Variant.Type.Int => _value.AsInt32(),
				Variant.Type.Float => _value.AsSingle(),
				Variant.Type.String => _value.AsString(),
				Variant.Type.Vector2 => _value.AsVector2(),
				Variant.Type.Vector3 => _value.AsVector3(),
				Variant.Type.Quaternion => _value.AsQuaternion(),
				Variant.Type.Color => _value.AsColor(),
				_ => null
			};
			property.SetValue(newValue);
		}

		Refresh();
	}

	private void OnInnerValueChanged(object? inner)
	{
		_value = ToGodot(inner);
		ValueChanged?.Invoke(_value);
	}

	public override void _Ready()
	{
		_popup = _variantEnumNode.GetPopup();

		foreach (string name in Enum.GetNames(typeof(VariantEnum)))
		{
			int id = (int)Enum.Parse(typeof(VariantEnum), name);
			_variantEnumNode.AddItem(name, id);
		}

		Apply();

		_popup.IdPressed += id =>
		{
			_value = DefaultFor((VariantEnum)(int)id);
			Apply();
			ValueChanged?.Invoke(_value);
		};
	}

	private static Control? CreateValueNode(VariantEnum type) => type switch
	{
		VariantEnum.Bool => Globals.CreateInstanceFromScene<BooleanProperty>(
			"res://scenes/creator/properties/BooleanProperty.tscn"),
		VariantEnum.Int => Globals.CreateInstanceFromScene<Int32Property>(
			"res://scenes/creator/properties/Int32Property.tscn"),
		VariantEnum.Float => Globals.CreateInstanceFromScene<SingleProperty>(
			"res://scenes/creator/properties/SingleProperty.tscn"),
		VariantEnum.String => Globals.CreateInstanceFromScene<StringProperty>(
			"res://scenes/creator/properties/StringProperty.tscn"),
		VariantEnum.Color => Globals.CreateInstanceFromScene<ColorProperty>(
			"res://scenes/creator/properties/ColorProperty.tscn"),
		VariantEnum.Vector2 => Globals.CreateInstanceFromScene<Vector2Property>(
			"res://scenes/creator/properties/Vector2Property.tscn"),
		VariantEnum.Vector3 => Globals.CreateInstanceFromScene<Vector3Property>(
			"res://scenes/creator/properties/Vector3Property.tscn"),
		VariantEnum.Quaternion => Globals.CreateInstanceFromScene<QuaternionProperty>(
			"res://scenes/creator/properties/QuaternionProperty.tscn"),
		_ => null
	};

	// Set default value when the user switches the type via the dropdown.
	private static Variant DefaultFor(VariantEnum type) => type switch
	{
		VariantEnum.Bool => false,
		VariantEnum.Int => 0,
		VariantEnum.Float => 0.0f,
		VariantEnum.String => "",
		VariantEnum.Vector2 => Vector2.Zero,
		VariantEnum.Vector3 => Vector3.Zero,
		VariantEnum.Quaternion => Quaternion.Identity,
		VariantEnum.Color => new Color(),
		_ => ToGodot(null)
	};
}
