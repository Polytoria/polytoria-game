// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class UnitQuaternionUInt64Dto
{
	[JsonInclude] public ulong Rotation { get; set; }

	// 16 bits per component packed into the 64-bit field.
	const int _quantMax = 65535; // (1 << 16) - 1

	[MemoryPackConstructor, JsonConstructor]
	public UnitQuaternionUInt64Dto() { }
	public UnitQuaternionUInt64Dto(Quaternion v) { Rotation = ToCompressed(v); }
	public Quaternion ToQuaternion() => FromCompressed(Rotation);

	public static string ToString(Quaternion src)
	{
		ulong compressed = ToCompressed(src);
		return $"{compressed}";
	}

	public static Quaternion FromString(string src)
	{
		return FromCompressed(Convert.ToUInt64(src));
	}

	public static ulong ToCompressed(Quaternion src)
	{
		ulong result = 0;
		for (int i = 0; i < 4; i++)
		{
			// Map each component from [-1, 1] to [0, 65535].
			ulong quantized = (ulong)Math.Clamp((int)((src[i] * 0.5f + 0.5f) * _quantMax + 0.5f), 0, _quantMax);
			result = (result << 16) | quantized;
		}
		return result;
	}

	public static Quaternion FromCompressed(ulong src)
	{
		Quaternion result = new();
		for (int i = 3; i >= 0; i--)
		{
			ulong quantized = src & _quantMax;
			src >>= 16;
			// Map each component from [0, 65535] back to [-1, 1].
			result[i] = (float)quantized / _quantMax * 2f - 1f;
		}
		return result.Normalized();
	}
}

public class UnitQuaternionUInt64JsonConverter : JsonConverter<Quaternion>
{
	public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array");
		}

		reader.Read();
		ulong compressed = reader.GetUInt64();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array");
		}

		return UnitQuaternionUInt64Dto.FromCompressed(compressed);
	}

	public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
	{
		ulong compressed = UnitQuaternionUInt64Dto.ToCompressed(value);
		writer.WriteStartArray();
		writer.WriteNumberValue(compressed);
		writer.WriteEndArray();
	}
}
