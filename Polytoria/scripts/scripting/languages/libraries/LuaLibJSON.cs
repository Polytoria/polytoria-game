// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Scripting.Libraries;

public class LuaLibJSON : IScriptObject
{
	[ScriptMethod("parse")]
	public static dynamic? Parse(string str)
	{
		JsonDocument doc = JsonDocument.Parse(str);
		JsonElement root = doc.RootElement;

		return JsonElementToObject(root);
	}

	[ScriptLegacyMethod("parse")]
	public static dynamic? LegacyParse(object? obj)
	{
		if (obj is not string str) throw new ArgumentException("Argument must be a JSON string");
		return Parse(str);
	}

	static object? JsonElementToObject(JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				Dictionary<string, object> dict = [];
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					object? v = JsonElementToObject(prop.Value);
					if (v != null)
						dict[prop.Name] = v;
				}
				return dict;

			case JsonValueKind.Array:
				List<object> list = [];
				foreach (JsonElement item in element.EnumerateArray())
				{
					object? v = JsonElementToObject(item);

					if (v != null)
						list.Add(v);
				}
				return list;

			case JsonValueKind.String:
				return element.GetString();

			case JsonValueKind.Number:
				// Use GetInt64, GetDouble, etc., depending on your needs
				return element.GetDecimal();

			case JsonValueKind.True:
			case JsonValueKind.False:
				return element.GetBoolean();

			case JsonValueKind.Null:
				return JSONNull.Value;
			default:
				return null;
		}
	}


	[ScriptMethod("serialize")]
	public static string Serialize(object? data)
	{
		if (data == null || data is JSONNull) return "null";
		try
		{
			return JsonSerializer.Serialize(data, typeof(object), LuaJSONGenerationContext.Default);
		}
		catch
		{
			if (Globals.IsBetaBuild)
			{
				// Throw literal error for debugging
				throw;
			}
			else
			{
				throw new InvalidOperationException("Tried to serialize an invalid JSON. Make sure your table only contains the primitive types (Instances are not supported)");
			}
		}
	}

	[ScriptMethod("null")]
	public static object Null()
	{
		return JSONNull.Value;
	}

	[ScriptMethod("isNull")]
	public static bool IsNull(object? value)
	{
		return value == null || value is JSONNull;
	}
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<object, object>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(JSONNull))]
internal partial class LuaJSONGenerationContext : JsonSerializerContext { }

[JsonConverter(typeof(JSONNullConverter))]
internal sealed class JSONNull : IScriptObject
{
	public static readonly JSONNull Value = new();
	JSONNull() { }
}

internal sealed class JSONNullConverter : JsonConverter<JSONNull>
{
	public override JSONNull? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> throw new NotSupportedException();

	public override void Write(Utf8JsonWriter writer, JSONNull value, JsonSerializerOptions options)
		=> writer.WriteNullValue();
}
