// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using static Polytoria.Scripting.Datatypes.PTVariant;

namespace Polytoria.Utils.DTOs;

// A Variant carries both a type enum and a value, so it cannot be reduced to a single fixed shape like the other DTOs.
// Only the marshallable types accepted by PTVariant are supported, anything else is rejected. The enum is stored as
// the PTVariant VariantEnum int and the inner value is encoded with that type's own representation.

[MemoryPackable]
public partial class VariantDto
{
	[JsonInclude] public int Type { get; set; }
	[JsonInclude] public string Value { get; set; } = "";

	[MemoryPackConstructor, JsonConstructor]
	public VariantDto() { }
	public VariantDto(Variant v) { Type = (int)ToScriptEnum(v.VariantType); Value = ToString(v); }
	public Variant ToVariant() => FromString((Variant.Type)Type, Value);

	public static string ToString(Variant src) => src.VariantType switch
	{
		Variant.Type.Nil => "",
		Variant.Type.Bool => src.AsBool() ? "true" : "false",
		Variant.Type.Int => src.AsInt32().ToString(CultureInfo.InvariantCulture),
		Variant.Type.Float => src.AsDouble().ToString("R", CultureInfo.InvariantCulture),
		Variant.Type.String => src.AsString(),
		Variant.Type.Vector2 => Vector2Dto.ToString(src.AsVector2()),
		Variant.Type.Vector3 => Vector3Dto.ToString(src.AsVector3()),
		Variant.Type.Quaternion => UnitQuaternionUInt64Dto.ToString(src.AsQuaternion()),
		Variant.Type.Color => ColorDto.ToString(src.AsColor()),
		_ => throw new ArgumentException($"Unsupported Variant type: {src.VariantType}")
	};

	public static Variant FromString(Variant.Type type, string src)
	{
		return type switch
		{
			Variant.Type.Nil => new Variant(),
			Variant.Type.Bool => bool.Parse(src),
			Variant.Type.Int => int.Parse(src, CultureInfo.InvariantCulture),
			Variant.Type.Float => double.Parse(src, CultureInfo.InvariantCulture),
			Variant.Type.String => src,
			Variant.Type.Vector2 => Vector2Dto.FromString(src),
			Variant.Type.Vector3 => Vector3Dto.FromString(src),
			Variant.Type.Quaternion => UnitQuaternionUInt64Dto.FromString(src),
			Variant.Type.Color => ColorDto.FromString(src),
			_ => throw new ArgumentException($"Unsupported Variant type: {type}"),
		};
	}
}

// Serialized as a [typeEnum, value] array: the first element is the PTVariant VariantEnum int, and the second is the
// inner value encoded by that type's converter. Nil is written as a single-element array.
public class VariantJsonConverter : JsonConverter<Variant>
{
	private static readonly Vector2JsonConverter Vector2Converter = new();
	private static readonly Vector3JsonConverter Vector3Converter = new();
	private static readonly UnitQuaternionUInt64JsonConverter QuaternionConverter = new();
	private static readonly ColorJsonConverter ColorConverter = new();

	public override Variant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array.");
		}

		reader.Read();
		Variant.Type type = (Variant.Type)reader.GetInt32();

		Variant value;
		if (type == Variant.Type.Nil)
		{
			value = new Variant();
		}
		else
		{
			reader.Read();
			value = type switch
			{
				Variant.Type.Bool => reader.GetBoolean(),
				Variant.Type.Int => reader.GetInt32(),
				Variant.Type.Float => reader.GetDouble(),
				Variant.Type.String => reader.GetString() ?? "",
				Variant.Type.Vector2 => Vector2Converter.Read(ref reader, typeof(Vector2), options),
				Variant.Type.Vector3 => Vector3Converter.Read(ref reader, typeof(Vector3), options),
				Variant.Type.Quaternion => QuaternionConverter.Read(ref reader, typeof(Quaternion), options),
				Variant.Type.Color => ColorConverter.Read(ref reader, typeof(Color), options),
				_ => throw new JsonException($"Unsupported Variant type: {type}"),
			};
		}

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array.");
		}

		return value;
	}

	public override void Write(Utf8JsonWriter writer, Variant value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue((int)ToScriptEnum(value.VariantType));
		switch (value.VariantType)
		{
			case Variant.Type.Nil: break;
			case Variant.Type.Bool: writer.WriteBooleanValue(value.AsBool()); break;
			case Variant.Type.Int: writer.WriteNumberValue(value.AsInt32()); break;
			case Variant.Type.Float: writer.WriteNumberValue(value.AsDouble()); break;
			case Variant.Type.String: writer.WriteStringValue(value.AsString()); break;
			case Variant.Type.Vector2: Vector2Converter.Write(writer, value.AsVector2(), options); break;
			case Variant.Type.Vector3: Vector3Converter.Write(writer, value.AsVector3(), options); break;
			case Variant.Type.Quaternion: QuaternionConverter.Write(writer, value.AsQuaternion(), options); break;
			case Variant.Type.Color: ColorConverter.Write(writer, value.AsColor(), options); break;
			default: throw new JsonException($"Unsupported Variant type: {value.VariantType}");
		}
		writer.WriteEndArray();
	}
}
