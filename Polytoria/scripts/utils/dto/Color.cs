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
public partial class ColorDto
{
	[JsonInclude] public uint Rgba { get; set; } = 0xFFFFFFFF;

	[MemoryPackConstructor, JsonConstructor]
	public ColorDto() { }
	public ColorDto(Color c)
	{
		Rgba =
			((uint)(byte)(c.R * 255) << 24) |
			((uint)(byte)(c.G * 255) << 16) |
			((uint)(byte)(c.B * 255) << 8) |
			(byte)(c.A * 255);
	}
	public Color ToColor()
	{
		return new Color(
			((Rgba >> 24) & 0xFF) / 255f,
			((Rgba >> 16) & 0xFF) / 255f,
			((Rgba >> 8) & 0xFF) / 255f,
			(Rgba & 0xFF) / 255f);
	}

	public static string ToString(Color src)
	{
		return src.ToHtml();
	}

	public static Color FromString(string src)
	{
		return Color.FromString(src, new(1, 1, 1));
	}
}

public class ColorJsonConverter : JsonConverter<Color>
{
	public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException("Expected string value");
		}

		string? hex = reader.GetString();

		return hex == null ? throw new JsonException("Expected hex value") : Color.FromString(hex, new(1, 1, 1));
	}

	public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToHtml());
	}
}
