// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace Polytoria.Shared.Settings;

internal static class SettingsFileUtility
{
	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
	internal static bool Save(string path, Dictionary<string, object?> values)
	{
		try
		{
			// Serialize BEFORE opening the file so a serialization failure doesnt break anything :pray:
			string json = JsonSerializer.Serialize(values);

			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			if (file == null)
			{
				PT.PrintErr($"FileAccess.Open returned null for path {path}");
				return false;
			}
			file.StoreString(json);
			return true;
		}
		catch (Exception e)
		{
			PT.PrintErr($"Failed to save settings to {path}: {e}");
			return false;
		}
	}

	internal static void Load(string path, Dictionary<string, object?> values, IReadOnlyDictionary<string, SettingDef> definitions)
	{
		if (!FileAccess.FileExists(path))
			return;

		try
		{
			string json = FileAccess.GetFileAsString(path);

			if (string.IsNullOrEmpty(json))
			{
				PT.PrintWarn($"Settings file at '{path}' is empty, removing and using defaults.");
				DirAccess.RemoveAbsolute(path);
				return;
			}

			using JsonDocument document = JsonDocument.Parse(json);

			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				PT.PrintWarn($"Settings file at '{path}' has unexpected root type {document.RootElement.ValueKind}, ignoring.");
				return;
			}

			foreach (JsonProperty property in document.RootElement.EnumerateObject())
			{
				if (!definitions.TryGetValue(property.Name, out var def))
					continue;

				try
				{
					object? parsed = ParseJsonElement(property.Value, def);
					values[property.Name] = def.ConvertToType(parsed);
				}
				catch (Exception e)
				{
					PT.PrintErr($"Failed to parse setting '{property.Name}': {e}");
				}
			}
		}
		catch (Exception e)
		{
			PT.PrintErr($"Failed to load settings from {path}: {e}");
		}
	}

	internal static object? ParseJsonElement(JsonElement el, SettingDef def)
	{
		if (!IsJsonKindCompatible(el.ValueKind, def.ValueKind))
		{
			PT.PrintErr($"Settings type mismatch for '{def.Key}': expected {def.ValueKind}, got {el.ValueKind}. Using default.");
			return def.UntypedDefault;
		}

		return def.ValueKind switch
		{
			SettingValueKind.Bool => el.GetBoolean(),
			SettingValueKind.Int => el.GetInt32(),
			SettingValueKind.Float => el.GetSingle(),
			SettingValueKind.String => el.GetString(),
			SettingValueKind.Enum => el.ValueKind switch
			{
				JsonValueKind.String => el.GetString(),
				JsonValueKind.Number => el.GetInt32(),
				_ => null
			},
			_ => null
		};
	}

	internal static object? ParseStringValue(string value, SettingDef def)
	{
		switch (def.ValueKind)
		{
			case SettingValueKind.Bool:
				if (bool.TryParse(value, out bool bv))
					return bv;
				PT.PrintErr($"Failed to parse bool setting '{def.Key}' from string value '{value}'.");
				return def.UntypedDefault;

			case SettingValueKind.Int:
				if (int.TryParse(value, out int iv))
					return iv;
				PT.PrintErr($"Failed to parse int setting '{def.Key}' from string value '{value}'.");
				return def.UntypedDefault;

			case SettingValueKind.Float:
				if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
					return fv;
				PT.PrintErr($"Failed to parse float setting '{def.Key}' from string value '{value}'.");
				return def.UntypedDefault;

			case SettingValueKind.String:
				return value;

			case SettingValueKind.Enum:
				if (int.TryParse(value, out int enumIv))
					return Enum.GetName(def.ValueType, enumIv) ?? value;
				return value;

			default:
				return value;
		}
	}

	private static bool IsJsonKindCompatible(JsonValueKind jsonKind, SettingValueKind valueKind)
	{
		return valueKind switch
		{
			SettingValueKind.Bool => jsonKind is JsonValueKind.True or JsonValueKind.False,
			SettingValueKind.Int => jsonKind == JsonValueKind.Number,
			SettingValueKind.Float => jsonKind == JsonValueKind.Number,
			SettingValueKind.String => jsonKind == JsonValueKind.String,
			SettingValueKind.Enum => jsonKind is JsonValueKind.String or JsonValueKind.Number,
			_ => false
		};
	}

}
