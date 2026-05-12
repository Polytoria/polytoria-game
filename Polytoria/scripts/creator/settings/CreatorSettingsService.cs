// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Godot;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Creator.Settings;

public sealed partial class CreatorSettingsService : SettingsServiceBase
{
	private const string SettingsPathConst = "user://creator/creator_settings.json";
	public static CreatorSettingsService Instance { get; private set; } = null!;

	private static readonly Dictionary<string, string> OldToNewKeyMap = new()
	{
		["Creator.OpenWebAfterPublish"] = CreatorSettingKeys.Creator.OpenWebAfterPublish,
		["Interface.UIScale"] = CreatorSettingKeys.Interface.UiScale,
		["Interface.UseFullscreen"] = SharedSettingKeys.Display.Fullscreen,
		["Backup.MaxBackupCount"] = CreatorSettingKeys.Backup.MaxBackupCount,
		["Backup.BackupInterval"] = CreatorSettingKeys.Backup.BackupInterval,
		["CodeEditor.PreferredEditor"] = CreatorSettingKeys.CodeEditor.PreferredEditor,
		["CodeEditor.IndentationMode"] = CreatorSettingKeys.CodeEditor.IndentationMode,
		["CodeEditor.IndentationSize"] = CreatorSettingKeys.CodeEditor.IndentationSize,
		["Graphics.VSync"] = SharedSettingKeys.Display.VSync,
		["Graphics.RenderingMethod"] = SharedSettingKeys.Graphics.RenderingMethod,
		["Popups.CloseModelWarning"] = CreatorSettingKeys.Popups.CloseModelWarning,
		["Popups.MoveFileConfirmation"] = CreatorSettingKeys.Popups.MoveFileConfirmation,
		["Popups.CloseTabWarning"] = CreatorSettingKeys.Popups.CloseTabWarning,
	};

	protected override string SettingsPath => SettingsPathConst;
	protected override IReadOnlyDictionary<string, SettingDef> Registry => CreatorSettingsRegistry.Definitions;

	public CreatorSettingsService()
	{
		Instance = this;
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	public void Init()
	{
		MigrateFromOldFormat();
		Load();
		ApplyDefaults();

		if (!FileAccess.FileExists(SettingsPathConst))
			Set(SharedSettingKeys.Graphics.Preset, GraphicsPreset.Medium);

		RenderingMethodOption renderingMethod = Get<RenderingMethodOption>(SharedSettingKeys.Graphics.RenderingMethod);
		RenderingDeviceSwitcher.Switch(RenderingDeviceSwitcher.FromRenderingMethodOption(renderingMethod));
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private void MigrateFromOldFormat()
	{
		const string oldFilePath = "user://creator/creator_settings";

		if (!FileAccess.FileExists(oldFilePath))
			return;

		PT.Print("Migrating creator settings from old format");

		try
		{
			string oldJson = FileAccess.GetFileAsString(oldFilePath);
			var oldData = JsonSerializer.Deserialize<Dictionary<string, string>>(oldJson);

			if (oldData == null || oldData.Count == 0)
				return;

			var newData = new Dictionary<string, object?>();

			if (oldData.TryGetValue("Graphics.PostProcessing", out string? postProcessingStr)
				&& bool.TryParse(postProcessingStr, out bool postProcessing))
			{
				newData[SharedSettingKeys.PostProcessing.Glow] = postProcessing;
				newData[SharedSettingKeys.PostProcessing.Ssao] = postProcessing;
			}

			if (oldData.TryGetValue("Graphics.PhotoMode", out string? photoModeStr)
				&& bool.TryParse(photoModeStr, out bool photoMode) && photoMode)
			{
				newData[SharedSettingKeys.PostProcessing.Ssr] = true;
				newData[SharedSettingKeys.PostProcessing.Sdfgi] = true;
				newData[SharedSettingKeys.PostProcessing.Ssil] = true;
			}

			foreach ((string oldKey, string oldValue) in oldData)
			{
				if (!OldToNewKeyMap.TryGetValue(oldKey, out string? newKey))
					continue;

				if (!CreatorSettingsRegistry.Definitions.TryGetValue(newKey, out var def))
					continue;

				newData[newKey] = SettingsFileUtility.ParseStringValue(oldValue, def);
			}

			if (newData.Count == 0)
				return;

			string newJson = JsonSerializer.Serialize(newData);
			using var newFile = FileAccess.Open(SettingsPathConst, FileAccess.ModeFlags.Write);
			newFile.StoreString(newJson);
			newFile.Close();

			DirAccess.RenameAbsolute(oldFilePath, "user://creator/creator_settings.old");

			PT.Print("Migrated creator settings from old format");
		}
		catch (Exception e)
		{
			PT.PrintErr($"Failed to migrate creator settings: {e}");
		}
	}

	protected override void OnAfterSet(string key, object normalizedValue)
	{
		GraphicsPresetManager.HandlePresetChange(this, key, normalizedValue);
	}
}
