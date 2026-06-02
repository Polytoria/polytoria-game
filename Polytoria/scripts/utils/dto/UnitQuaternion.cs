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
public partial class UnitQuaternionDto
{
	[JsonInclude] public uint Rotation { get; set; }

	// Constant used for compression.
	static readonly float _oneSqrtTwo = (float)(1.0f / Math.Sqrt(2.0f));

	[MemoryPackConstructor, JsonConstructor]
	public UnitQuaternionDto() { }
	public UnitQuaternionDto(Quaternion v) { Rotation = ToCompressed(v); }
	public Quaternion ToQuaternion() => FromCompressed(Rotation);

	public static string ToString(Quaternion src)
	{
		uint compressed = ToCompressed(src);
		return $"{compressed}";
	}

	public static Quaternion FromString(string src)
	{
		return FromCompressed(Convert.ToUInt32(src));
	}

	public static uint ToCompressed(Quaternion src)
	{
		int largestComponent = 0;
		for (int i = 1; i < 4; i++)
		{
			if (Math.Abs(src[i]) > Math.Abs(src[largestComponent])) { largestComponent = i; }
		}

		uint negate = (uint)(src[largestComponent] < 0 ? 1 : 0);
		uint result = (uint)largestComponent;
		for (int i = 0; i < 4; i++)
		{
			if (i != largestComponent)
			{
				uint negbit = (uint)(src[i] < 0 ? 0x1 : 0x0) ^ negate;
				// 511 == ((1 << 9) - 1)
				uint magnitude = (uint)(511 * Math.Abs(src[i]) / _oneSqrtTwo + 0.5f);
				result = (result << 10) | (negbit << 9) | magnitude;
			}
		}
		return result;
	}

	public static Quaternion FromCompressed(uint src)
	{
		Quaternion result = new Quaternion();
		int largestComponent = (int)(src >> 30);
		double sumSquares = 0;
		for (int i = 3; i >= 0; i--)
		{
			if (i != largestComponent)
			{
				uint magnitude = src & 511;
				uint negbit = (src >> 9) & 1;
				src = src >> 10;
				result[i] = _oneSqrtTwo * (float)magnitude / 511;
				if (negbit == (uint)1) { result[i] = -result[i]; }
				sumSquares += result[i] * result[i];
			}
		}
		result[largestComponent] = (float)Math.Sqrt(1 - sumSquares);
		return result.Normalized();
	}
}

public class UnitQuaternionJsonConverter : JsonConverter<Quaternion>
{
	public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected start of array");
		}

		reader.Read();
		uint compressed = reader.GetUInt32();

		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException("Expected end of array");
		}

		return UnitQuaternionDto.FromCompressed(compressed);
	}

	public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
	{
		uint compressed = UnitQuaternionDto.ToCompressed(value);
		writer.WriteStartArray();
		writer.WriteNumberValue(compressed);
		writer.WriteEndArray();
	}
}
