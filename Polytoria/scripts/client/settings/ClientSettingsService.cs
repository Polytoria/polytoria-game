using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace Polytoria.Client.Settings;

public sealed partial class ClientSettingsService : Node, ISettingsContext
{
	private const string SettingsPath = "user://settings_client.json";
	public static ClientSettingsService Instance { get; private set; } = null!;

	private readonly Dictionary<string, object?> _values = [];
	private bool _saveQueued;
	private int _presetCounter;

	public event Action<SettingChangedEvent>? Changed;

	public ClientEntry Entry { get; init; } = null!;

	public ClientSettingsService()
	{
		Instance = this;
	}

	public void Init()
	{
		bool settingsExists = FileAccess.FileExists(SettingsPath);
		Load();
		ApplyDefaults();

		if (!settingsExists)
		{
			GraphicsPreset autoPreset = GraphicsAutoDetector.Detect();
			ApplyGraphicsPreset(autoPreset);

			GraphicsBenchmarker benchmarker = new GraphicsBenchmarker();
			AddChild(benchmarker);

			PT.Print("Graphics auto-detection selected preset: " + autoPreset);

			if (Entry != null)
			{
				Entry.NetworkEssentialsReady += () =>
				{
					World? world = World.Current;
					if (world == null)
					{
						PT.PrintErr("Cannot profile graphics because there is no world");
						return;
					}

					bool profiling = false;

					void StartProfile()
					{
						if (profiling)
						{
							return;
						}
						profiling = true;
						GraphicsPreset current = Get<GraphicsPreset>(ClientSettingKeys.Graphics.Preset);
						if (current == GraphicsPreset.Custom) return;
						PT.Print("Starting graphics benchmark...");
						benchmarker.Start();
						benchmarker.Finished += (avgFps) =>
						{
							profiling = false;
							PT.Print($"Graphics benchmark finished. Average FPS: {avgFps}");
							if (avgFps <= 40f)
							{
								GraphicsPreset lower = current switch
								{
									GraphicsPreset.Ultra => GraphicsPreset.High,
									GraphicsPreset.High => GraphicsPreset.Medium,
									GraphicsPreset.Medium => GraphicsPreset.Low,
									_ => current
								};

								ApplyGraphicsPreset(lower);
							}
							else if (avgFps >= 55f)
							{
								GraphicsPreset higher = current switch
								{
									GraphicsPreset.Low => GraphicsPreset.Medium,
									GraphicsPreset.Medium => GraphicsPreset.High,
									_ => current
								};

								ApplyGraphicsPreset(higher);
							}
						};
					}


					if (world.IsLoaded)
					{
						StartProfile();
					}
					else
					{
						world.Loaded.Connect(StartProfile);
					}
				};
			}
		}

		RenderingMethodOption renderingMethod = Get<RenderingMethodOption>(ClientSettingKeys.Graphics.RenderingMethod);
		RenderingDeviceSwitcher.RenderingDeviceEnum gdMethod = renderingMethod switch
		{
			RenderingMethodOption.Standard => RenderingDeviceSwitcher.RenderingDeviceEnum.Forward,
			RenderingMethodOption.Performance => RenderingDeviceSwitcher.RenderingDeviceEnum.Mobile,
			RenderingMethodOption.Compatibility => RenderingDeviceSwitcher.RenderingDeviceEnum.GLCompatibility,
			_ => RenderingDeviceSwitcher.RenderingDeviceEnum.Forward
		};

		RenderingDeviceSwitcher.Switch(gdMethod);

		if (!settingsExists)
		{
			QueueSave();
		}
	}

	public T Get<T>(string key)
	{
		if (_values.TryGetValue(key, out object? value) && value is T typed)
		{
			return typed;
		}

		var def = ClientSettingsRegistry.Definitions[key];
		return (T)def.UntypedDefault;
	}

	public object? GetUntyped(string key)
	{
		if (_values.TryGetValue(key, out object? value))
		{
			return value;
		}

		return ClientSettingsRegistry.Definitions[key].UntypedDefault;
	}

	public void Set<T>(string key, T value)
	{
		var def = ClientSettingsRegistry.Definitions[key];
		object normalized = def.ConvertToType(value);

		object? oldValue = GetUntyped(key);
		if (Equals(oldValue, normalized))
		{
			return;
		}

		_values[key] = normalized;
		Changed?.Invoke(new SettingChangedEvent(key, oldValue, normalized, def.RequiresRestart));
		HandleGraphicsSettingChange(key, normalized);
		QueueSave();
	}

	private void HandleGraphicsSettingChange(string key, object normalizedValue)
	{
		if (key == ClientSettingKeys.Graphics.Preset)
		{
			ApplyGraphicsPreset((GraphicsPreset)normalizedValue);
			return;
		}

		if (_presetCounter > 0) // If we're in the middle of applying a preset, return
		{
			return;
		}

		if (!GraphicsPresetManager.IsPresetManagedKey(key))
		{
			return;
		}

		GraphicsPreset currentPreset = Get<GraphicsPreset>(ClientSettingKeys.Graphics.Preset);
		if (currentPreset != GraphicsPreset.Custom)
		{
			Set(ClientSettingKeys.Graphics.Preset, GraphicsPreset.Custom);
		}
	}

	private void ApplyGraphicsPreset(GraphicsPreset preset)
	{
		PT.Print($"Applying graphics preset: {preset}");
		if (preset == GraphicsPreset.Custom)
		{
			return;
		}

		_presetCounter++;
		try
		{
			GraphicsPresetManager.ApplyPreset(preset);
		}
		finally
		{
			_presetCounter--;
		}
	}

	public void Reset(string key)
	{
		var def = ClientSettingsRegistry.Definitions[key];
		Set(key, def.UntypedDefault);
	}

	public void Save()
	{
		using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();

			foreach (var pair in _values)
			{
				writer.WritePropertyName(pair.Key);
				WriteJsonValue(writer, pair.Value);
			}

			writer.WriteEndObject();
		}

		file.StoreString(Encoding.UTF8.GetString(stream.ToArray()));
		file.Close();
	}

	private void QueueSave()
	{
		if (_saveQueued)
		{
			return;
		}

		_saveQueued = true;

		Callable.From(() =>
		{
			_saveQueued = false;
			Save();
		}).CallDeferred();
	}

	private void Load()
	{
		if (!FileAccess.FileExists(SettingsPath))
		{
			return;
		}

		try
		{
			string json = FileAccess.GetFileAsString(SettingsPath);
			using JsonDocument document = JsonDocument.Parse(json);

			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			foreach (JsonProperty property in document.RootElement.EnumerateObject())
			{
				if (ClientSettingsRegistry.Definitions.TryGetValue(property.Name, out var def))
				{
					object? parsed = ParseJsonElement(property.Value, def);
					_values[property.Name] = def.ConvertToType(parsed);
				}
			}
		}
		catch (Exception e)
		{
			GD.PushWarning($"Failed to load client settings: {e}");
		}
	}

	private void ApplyDefaults()
	{
		foreach (var pair in ClientSettingsRegistry.Definitions)
		{
			if (!_values.ContainsKey(pair.Key))
			{
				_values[pair.Key] = pair.Value.UntypedDefault;
			}
		}
	}

	private static object? ParseJsonElement(JsonElement el, SettingDef def)
	{
		return def.ValueKind switch
		{
			SettingValueKind.Bool => el.GetBoolean(),
			SettingValueKind.Int => el.GetInt32(),
			SettingValueKind.Float => el.GetSingle(),
			SettingValueKind.String => el.GetString(),
			SettingValueKind.Enum => ParseEnumValue(el, def.UntypedDefault.GetType()),
			_ => null
		};
	}

	private static object? ParseEnumValue(JsonElement el, Type enumType)
	{
		return el.ValueKind switch
		{
			JsonValueKind.String => Enum.Parse(enumType, el.GetString()!, true),
			JsonValueKind.Number => Enum.ToObject(enumType, el.GetInt32()),
			_ => null
		};
	}

	private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
	{
		switch (value)
		{
			case null:
				writer.WriteNullValue();
				break;
			case bool boolValue:
				writer.WriteBooleanValue(boolValue);
				break;
			case byte byteValue:
				writer.WriteNumberValue(byteValue);
				break;
			case short shortValue:
				writer.WriteNumberValue(shortValue);
				break;
			case int intValue:
				writer.WriteNumberValue(intValue);
				break;
			case long longValue:
				writer.WriteNumberValue(longValue);
				break;
			case float floatValue:
				writer.WriteNumberValue(floatValue);
				break;
			case double doubleValue:
				writer.WriteNumberValue(doubleValue);
				break;
			case decimal decimalValue:
				writer.WriteNumberValue(decimalValue);
				break;
			case string stringValue:
				writer.WriteStringValue(stringValue);
				break;
			case Enum enumValue:
				writer.WriteStringValue(enumValue.ToString());
				break;
			default:
				writer.WriteStringValue(value.ToString());
				break;
		}
	}
}
