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
	[JsonInclude] public ulong Rotation { get; set; }

	// Constant used for compression.
	static readonly float _oneSqrtTwo = (float)(1.0f / Math.Sqrt(2.0f));

	[MemoryPackConstructor, JsonConstructor]
	public UnitQuaternionDto() { }
	public UnitQuaternionDto(Quaternion v) { Rotation = ToCompressed(v); }

	public Quaternion ToQuaternion() => FromCompressed(Rotation);

	public static string ToString(Quaternion src) => $"{ToCompressed(src)}";
	public static Quaternion FromString(string src) => FromCompressed(Convert.ToUInt32(src));

	public static ulong ToCompressed(Quaternion src)
	{
		int largestComponent = 0;
		for (int i = 1; i < 4; i++)
		{
			if (Math.Abs(src[i]) > Math.Abs(src[largestComponent])) { largestComponent = i; }
		}

		// NOTE: A quaternion is equal to it's inverse.
		ulong negate = (ulong)(src[largestComponent] < 0 ? 1 : 0);
		ulong result = ((ulong)largestComponent) << 2;
		for (int i = 0; i < 4; i++)
		{
			if (i != largestComponent)
			{
				ulong negbit = (ulong)(src[i] < 0 ? 0x1 : 0x0) ^ negate;
				// 524287 == ((1 << 19) - 1)
				ulong magnitude = (ulong)(524287 * Math.Abs(src[i]) / _oneSqrtTwo + 0.5f);
				result = (result << 20) | (negbit << 19) | magnitude;
			}
		}
		return result;
	}

	public static Quaternion FromCompressed(ulong src)
	{
		Quaternion result = new Quaternion();
		int largestComponent = (int)(src >> 62);
		double sumSquares = 0;
		for (int i = 3; i >= 0; i--)
		{
			if (i != largestComponent)
			{
				ulong magnitude = src & 524287;
				ulong negbit = (src >> 19) & 1;
				src >>= 20;
				result[i] = _oneSqrtTwo * (float)magnitude / 524287;
				if (negbit == 0x1) result[i] = -result[i];
				sumSquares += result[i] * result[i];
			}
		}
		result[largestComponent] = (float)Math.Sqrt(Math.Max(0, 1 - sumSquares));
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
		ulong compressed = UnitQuaternionDto.ToCompressed(value);
		writer.WriteStartArray();
		writer.WriteNumberValue(compressed);
		writer.WriteEndArray();
	}
}
