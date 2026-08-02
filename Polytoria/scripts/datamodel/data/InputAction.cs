// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using Polytoria.Scripting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Datamodel.Data;

public class InputButtonCollection : IEnumerable, IScriptObject
{
	private readonly List<InputButton> _buttons = [];

	public InputButtonCollection() { }

	public InputButtonCollection(List<InputButton> btns)
	{
		_buttons = btns;
	}

	[ScriptMethod]
	public void AddButton(InputButton btn)
	{
		foreach (InputButton item in _buttons.ToArray())
		{
			if (item.Equals(btn))
			{
				_buttons.Remove(btn);
			}
		}

		_buttons.Add(btn);
	}

	[ScriptMethod]
	public void RemoveButton(InputButton btn)
	{
		_buttons.Remove(btn);
	}

	[ScriptMethod]
	public InputButton[] GetButtons()
	{
		return [.. _buttons];
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _buttons.GetEnumerator();
	}
}


[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(InputActionVector2), "Vector2")]
[JsonDerivedType(typeof(InputActionButton), "Button")]
[JsonDerivedType(typeof(InputActionAxis), "Axis")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public abstract class InputAction : IScriptObject
{
	private string _name = "";
	public string Name
	{
		get => _name;
		set
		{
			_name = value;
			Renamed?.Invoke();
		}
	}

	public event Action? Renamed;
	[JsonIgnore] public InputService InputService = null!;
}

public class InputButton : IScriptObject
{
	[ScriptProperty] public KeyCodeEnum KeyCode { get; set; } = KeyCodeEnum.None;

	[ScriptMethod]
	public static InputButton New()
	{
		return new();
	}

	[ScriptMethod]
	public static InputButton New(KeyCodeEnum key)
	{
		return new() { KeyCode = key };
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool MetamethodEquals(InputButton a, InputButton b)
	{
		return a.Equals(b);
	}

	public override bool Equals(object? obj)
	{
		return obj is InputButton b && b.KeyCode.Equals(KeyCode);
	}

	public override int GetHashCode()
	{
		return KeyCode.GetHashCode();
	}
}

public class InputActionVector2 : InputAction
{
	[ScriptProperty] public InputButtonCollection Up { get; set; } = [];
	[ScriptProperty] public InputButtonCollection Down { get; set; } = [];
	[ScriptProperty] public InputButtonCollection Left { get; set; } = [];
	[ScriptProperty] public InputButtonCollection Right { get; set; } = [];

	[ScriptProperty, JsonIgnore] public Vector2 Value { get; internal set; }
}

public class InputActionButton : InputAction
{
	[ScriptProperty] public InputButtonCollection Buttons { get; set; } = [];

	[ScriptProperty, JsonIgnore] public bool IsPressed { get; set; }
	[ScriptProperty, JsonIgnore] public float Weight { get; set; }

	[ScriptProperty, JsonIgnore] public PTSignal Pressed { get; private set; } = new();
	[ScriptProperty, JsonIgnore] public PTSignal Released { get; private set; } = new();
}

public class InputActionAxis : InputAction
{
	[ScriptProperty] public InputButtonCollection Negative { get; set; } = [];
	[ScriptProperty] public InputButtonCollection Positive { get; set; } = [];

	[ScriptProperty, JsonIgnore] public float Value { get; internal set; }
}

public class InputMapData
{
	public List<InputAction> Actions { get; set; } = [];

	public InputAction? FindAction(string name)
	{
		return Actions.FirstOrDefault((a) => a.Name == name);
	}

	public InputActionButton BindButton(string name)
	{
		InputActionButton action = new() { Name = name };
		Actions.Add(action);
		return action;
	}

	public InputActionAxis BindAxis(string name)
	{
		InputActionAxis action = new() { Name = name };
		Actions.Add(action);
		return action;
	}

	public InputActionVector2 BindVector2(string name)
	{
		InputActionVector2 action = new() { Name = name };
		Actions.Add(action);
		return action;
	}

	public static InputMapData LoadFromString(string str)
	{
		return JsonSerializer.Deserialize(str, InputActionsGenerationContext.Default.InputMapData) ?? new();
	}

	public string SaveToString()
	{
		return JsonSerializer.Serialize(this, InputActionsGenerationContext.Default.InputMapData);
	}
}

public class InputButtonCollectionJsonConverter : JsonConverter<InputButtonCollection>
{
	public override InputButtonCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array");
		}

		List<InputButton> buttons = [];

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndArray)
			{
				return new InputButtonCollection(buttons);
			}

			InputButton? button = JsonSerializer.Deserialize(ref reader, InputActionsGenerationContext.Default.InputButton);
			if (button != null)
			{
				buttons.Add(button);
			}
		}

		throw new JsonException("Expected end of array");
	}

	public override void Write(Utf8JsonWriter writer, InputButtonCollection value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (InputButton button in value)
		{
			JsonSerializer.Serialize(writer, button, InputActionsGenerationContext.Default.InputButton);
		}

		writer.WriteEndArray();
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, Converters = [typeof(InputButtonCollectionJsonConverter)])]
[JsonSerializable(typeof(InputMapData))]
[JsonSerializable(typeof(InputAction))]
[JsonSerializable(typeof(InputButton))]

[JsonSerializable(typeof(InputActionVector2))]
[JsonSerializable(typeof(InputActionButton))]
[JsonSerializable(typeof(InputActionAxis))]

[JsonSerializable(typeof(List<InputAction>))]
[JsonSerializable(typeof(InputButtonCollection))]

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(KeyCodeEnum))]
public partial class InputActionsGenerationContext : JsonSerializerContext { }
