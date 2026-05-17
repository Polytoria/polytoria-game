// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class Vector3Dto
{
	[JsonInclude] public float X { get; set; }
	[JsonInclude] public float Y { get; set; }
	[JsonInclude] public float Z { get; set; }

	[MemoryPackConstructor, JsonConstructor]
	public Vector3Dto() { }
	public Vector3Dto(Vector3 v) { X = v.X; Y = v.Y; Z = v.Z; }
	public Vector3 ToVector3() => new(X, Y, Z);

	public static string ToString(Vector3 src)
	{
		// Limit to 4 decimals
		return string.Join(",",
			src.X.ToString("0.####", CultureInfo.InvariantCulture),
			src.Y.ToString("0.####", CultureInfo.InvariantCulture),
			src.Z.ToString("0.####", CultureInfo.InvariantCulture)
		);
	}

	public static Vector3 FromString(string src)
	{
		string[] parts = src.Split(',');
		return new Vector3(
			float.Parse(parts[0], CultureInfo.InvariantCulture),
			float.Parse(parts[1], CultureInfo.InvariantCulture),
			float.Parse(parts[2], CultureInfo.InvariantCulture)
		);
	}
}

public class Vector3JsonConverter : JsonConverter<Vector3>
{
	public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array");
		}

		reader.Read();
		float x = reader.GetSingle();

		reader.Read();
		float y = reader.GetSingle();

		reader.Read();
		float z = reader.GetSingle();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array");
		}

		return new Vector3(x, y, z);
	}

	public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		writer.WriteNumberValue(value.Z);
		writer.WriteEndArray();
	}
}
