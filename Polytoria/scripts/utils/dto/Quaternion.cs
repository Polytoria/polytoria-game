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
public partial class QuaternionDto
{
	[JsonInclude] public float X { get; set; }
	[JsonInclude] public float Y { get; set; }
	[JsonInclude] public float Z { get; set; }
	[JsonInclude] public float W { get; set; }

	[MemoryPackConstructor, JsonConstructor]
	public QuaternionDto() { }
	public QuaternionDto(Quaternion v) { X = v.X; Y = v.Y; Z = v.Z; W = v.W; }
	public Quaternion ToQuaternion() => new(X, Y, Z, W);

	public static string ToString(Quaternion src)
	{
		// Limit to 5 decimals; orientation error of ~0.001 degrees, nearly matching Euler angle precision.
		return string.Join(",",
			src.X.ToString("0.#####", CultureInfo.InvariantCulture),
			src.Y.ToString("0.#####", CultureInfo.InvariantCulture),
			src.Z.ToString("0.#####", CultureInfo.InvariantCulture),
			src.W.ToString("0.#####", CultureInfo.InvariantCulture)
		);
	}

	public static Quaternion FromString(string src)
	{
		string[] parts = src.Split(',');
		Quaternion v = new Quaternion(
			float.Parse(parts[0], CultureInfo.InvariantCulture),
			float.Parse(parts[1], CultureInfo.InvariantCulture),
			float.Parse(parts[2], CultureInfo.InvariantCulture),
			float.Parse(parts[3], CultureInfo.InvariantCulture)
		);
		return v.Normalized();
	}
}

public class QuaternionJsonConverter : JsonConverter<Quaternion>
{
	public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
		float w = reader.GetSingle();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array");
		}

		return new Quaternion(x, y, z, w);
	}

	public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		writer.WriteNumberValue(value.Z);
		writer.WriteNumberValue(value.W);
		writer.WriteEndArray();
	}
}
