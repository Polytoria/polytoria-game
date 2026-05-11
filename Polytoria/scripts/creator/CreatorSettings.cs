// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Polytoria.Creator;

public sealed partial class CreatorSettings : Node
{
	private const string CreatorSettingsPath = "user://creator/creator_settings";
	public static CreatorSettings Singleton { get; private set; } = null!;
	private readonly SettingsRoot _root = null!;

	public SettingsRoot Root => _root;

	public CreatorSettings()
	{
		Singleton = this;
		_root = new()
		{
			Categories = [
				new() {
					Name = "Creator",
					DisplayName = "Creator",
					Settings = [
						new("OpenWebAfterPublish", "Open Web after Publish", true),
					]
				},
				new() {
					Name = "Interface",
					DisplayName = "Interface",
					Settings = [
						new("UIScale", "UI Scale", 1.0f) { MinValue = 0.5f, MaxValue = 5f },
						new("UseFullscreen", "Fullscreen", false),
					]
				},
				new() {
					Name = "Backup",
					DisplayName = "Backup",
					Settings = [
						new("MaxBackupCount", "Max Backup Count", 10),
						new("BackupInterval", "Backup Interval (minutes)", 4f)
					]
				},
				new() {
					Name = "CodeEditor",
					DisplayName = "Code Editor",
					Settings = [
						new("PreferredEditor", "Preferred Editor", PreferredEditorEnum.BuiltIn),
						new("IndentationMode", "Indentation Mode", IndentationModeEnum.Tabs),
						// TODO: Cap IndentationSize between 1 and 8. IndentationSize can be negative.
						// MinValue and MaxValue is bugged for integer settings.
						new("IndentationSize", "Indentation Size (In Spaces)", 2) { MinValue = 1, MaxValue = 8 },
					]
				},
				new() {
					Name = "Graphics",
					DisplayName = "Graphics",
					Settings = [
						new("PhotoMode", "Photo Mode", false),
						new("PostProcessing", "Post Processing", false),
						new("VSync", "V-Sync", true),
						new("RenderingMethod", "Rendering Method (requires restart)", RenderingMethodEnum.Standard),
					]
				},
				new() {
					Name = "Popups",
					DisplayName = "Popups",
					Settings = [
						new("CloseModelWarning", "Close Model Warning", true),
						new("MoveFileConfirmation", "Move File Confirmation", true),
						new("CloseTabWarning", "Close Tab Warning", true),
					]
				},
			]
		};

		if (FileAccess.FileExists(CreatorSettingsPath))
		{
			PT.Print("Loading creator settings...");
			try
			{
				Dictionary<string, string>? rawData = JsonSerializer.Deserialize(FileAccess.GetFileAsString(CreatorSettingsPath), CreatorSettingsGenerationContext.Default.DictionaryStringString);

				if (rawData != null)
				{
					foreach ((string key, string val) in rawData)
					{
						SetSettingJSON(key, val);
					}
				}

				PT.Print("Creator settings loaded!");
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}

		// Switch rendering method
		RenderingDeviceSwitcher.Switch(
			GetSetting<RenderingMethodEnum>("Graphics.RenderingMethod") switch
			{
				RenderingMethodEnum.Standard => RenderingDeviceSwitcher.RenderingDeviceEnum.Forward,
				RenderingMethodEnum.Performance => RenderingDeviceSwitcher.RenderingDeviceEnum.Mobile,
				RenderingMethodEnum.Compatibility => RenderingDeviceSwitcher.RenderingDeviceEnum.GLCompatibility,
				_ => RenderingDeviceSwitcher.RenderingDeviceEnum.Mobile,
			}
			);

		Globals.BeforeQuit += SaveSettings;
	}

	public void SaveSettings()
	{
		Dictionary<string, string> data = [];
		foreach (SettingsCategory cat in _root.Categories)
		{
			foreach (SettingsProperty s in cat.Settings)
			{
				data[cat.Name + "." + s.Name] = s.JSONValue;
			}
		}

		using FileAccess settingsFile = FileAccess.Open(CreatorSettingsPath, FileAccess.ModeFlags.Write);
		settingsFile.StoreString(JsonSerializer.Serialize(data, CreatorSettingsGenerationContext.Default.DictionaryStringString));
		settingsFile.Close();
		PT.Print("Creator settings saved!");
	}

	public void SetSetting(string propertyName, object value)
	{
		PT.Print($"Set setting: {propertyName} to: {value}");

		string[] parts = propertyName.Split('.');
		SettingsCategory? category = _root.GetCategory(parts[0]);

		if (category != null)
		{
			SettingsProperty? prop = category.GetSetting(parts[1]);
			prop?.Value = value;
		}
	}

	private void SetSettingJSON(string propertyName, string value)
	{
		string[] parts = propertyName.Split('.');
		SettingsCategory? category = _root.GetCategory(parts[0]);

		if (category != null)
		{
			SettingsProperty? prop = category.GetSetting(parts[1]);
			prop?.JSONValue = value;
		}
	}


	public object? GetSetting(string propertyName)
	{
		return GetSettingProperty(propertyName)?.Value;
	}

	public SettingsProperty? GetSettingProperty(string propertyName)
	{
		string[] parts = propertyName.Split('.');
		SettingsCategory? category = _root.GetCategory(parts[0]);

		if (category != null)
		{
			SettingsProperty? prop = category.GetSetting(parts[1]);

			if (prop != null)
			{
				return prop;
			}
		}
		return null;
	}

	public T? GetSetting<T>(string propertyName)
	{
		return (T?)GetSetting(propertyName);
	}

	public class SettingsRoot
	{
		public List<SettingsCategory> Categories { get; set; } = [];

		public void Add(SettingsCategory setting)
		{
			Categories.Add(setting);
		}

		public SettingsCategory? GetCategory(string name)
		{
			return Categories.FirstOrDefault(p => p.Name == name);
		}
	}

	public class SettingsCategory
	{
		public List<SettingsProperty> Settings { get; set; } = [];

		[JsonInclude] public string Name { get; set; } = "";
		public string DisplayName = "";
		public string Description = "";

		public void Add(SettingsProperty setting)
		{
			Settings.Add(setting);
		}

		public SettingsProperty? GetSetting(string name)
		{
			return Settings.FirstOrDefault(p => p.Name == name);
		}
	}

	public class SettingsProperty
	{
		private object? _value;

		public Type ValueType = null!;
		public object DefaultValue = null!;
		public string Name { get; set; } = null!;
		public string DisplayName = null!;
		public string Description = null!;

		public float MinValue = 0;
		public float MaxValue = 0;

		public object? Value
		{
			get => _value;
			set
			{
				_value = value;
				ValueChanged?.Invoke(value);
			}
		}

		public string JSONValue
		{
			get
			{
				JsonTypeInfo? typeInfo = CreatorSettingsGenerationContext.Default.GetTypeInfo(ValueType);
				if (typeInfo != null)
				{
					return JsonSerializer.Serialize(_value, typeInfo);
				}
				else
				{
					PT.PrintWarn("CreatorSettings: No type info available for ", ValueType);
				}
				return string.Empty;
			}
			set
			{
				JsonTypeInfo? typeInfo = CreatorSettingsGenerationContext.Default.GetTypeInfo(ValueType);
				if (typeInfo != null)
				{
					Value = JsonSerializer.Deserialize(value, typeInfo);
				}
			}
		}

		public event Action<object?>? ValueChanged;

		public SettingsProperty(string name, string displayName, object defaultValue, string? description = null)
		{
			Name = name;
			DisplayName = displayName;
			DefaultValue = defaultValue;
			ValueType = defaultValue.GetType();
			Value = defaultValue;
			Description = description ?? "";
		}

		public void Reset()
		{
			Value = DefaultValue;
		}
	}
}

public enum PreferredEditorEnum
{
	BuiltIn,
	SystemDefault,
	VSCode,
	Zed
}

public enum IndentationModeEnum
{
	Tabs,
	Spaces
}

public enum RenderingMethodEnum
{
	Standard,
	Performance,
	Compatibility
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(object))]

[JsonSerializable(typeof(PreferredEditorEnum))]
[JsonSerializable(typeof(IndentationModeEnum))]
[JsonSerializable(typeof(RenderingMethodEnum))]
public partial class CreatorSettingsGenerationContext : JsonSerializerContext { }
