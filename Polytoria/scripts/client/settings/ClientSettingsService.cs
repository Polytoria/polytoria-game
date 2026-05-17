// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.Settings;

public sealed partial class ClientSettingsService : SettingsServiceBase
{
	private const string SettingsPathConst = "user://settings_client.json";
	private const string RenderingMethodMigrationPath = "user://rendering_method_migration";
	public static ClientSettingsService Instance { get; private set; } = null!;

	public ClientEntry Entry { get; init; } = null!;

	protected override string SettingsPath => SettingsPathConst;
	protected override IReadOnlyDictionary<string, SettingDef> Registry => ClientSettingsRegistry.Definitions;

	public ClientSettingsService()
	{
		Instance = this;
	}

	public void Init()
	{
		bool settingsExists = FileAccess.FileExists(SettingsPathConst);
		Load();
		MigrateRenderingMethod();
		ApplyDefaults();

		if (!settingsExists)
		{
			GraphicsPreset autoPreset = GraphicsAutoDetector.Detect();
			Set(SharedSettingKeys.Graphics.Preset, autoPreset);

			GraphicsBenchmarker benchmarker = new GraphicsBenchmarker();
			AddChild(benchmarker);

			PT.Print("Graphics auto-detection selected preset: " + autoPreset);
			if (Entry != null)
				Entry.NetworkEssentialsReady += () => SetupBenchmark(benchmarker);
		}

		RenderingMethodOption renderingMethod = Get<RenderingMethodOption>(SharedSettingKeys.Graphics.RenderingMethod);
		RenderingDeviceSwitcher.Switch(renderingMethod);

		if (!settingsExists)
			QueueSave();
	}

	private void MigrateRenderingMethod()
	{
		if (FileAccess.FileExists(RenderingMethodMigrationPath))
		{
			return;
		}

		try
		{
			if (_values.TryGetValue(SharedSettingKeys.Graphics.RenderingMethod, out object? rawValue))
			{
				RenderingMethodOption renderingMethod = (RenderingMethodOption)ClientSettingsRegistry.Definitions[SharedSettingKeys.Graphics.RenderingMethod].ConvertToType(rawValue);
				PT.Print("Current rendering method: " + renderingMethod);
				if (renderingMethod == RenderingMethodOption.Standard)
				{
					Set(SharedSettingKeys.Graphics.RenderingMethod, RenderingMethodOption.Auto);
					PT.Print("Migrated rendering method setting to Auto");
				}
				else
				{
					PT.Print("No migration needed for rendering method");
				}
			}

			using var file = FileAccess.Open(RenderingMethodMigrationPath, FileAccess.ModeFlags.Write);
			file.StoreString(Globals.AppVersion);
		}
		catch (Exception e)
		{
			PT.PrintErr("Failed to migrate rendering method setting: " + e);
		}
	}

	private void SetupBenchmark(GraphicsBenchmarker benchmarker)
	{
		World? world = World.Current;
		if (world == null)
		{
			PT.PrintErr("Cannot profile graphics because there is no world");
			return;
		}

		void StartProfile()
		{
			GraphicsPreset current = Get<GraphicsPreset>(SharedSettingKeys.Graphics.Preset);
			if (current == GraphicsPreset.Custom)
			{
				benchmarker.QueueFree();
				return;
			}

			PT.Print("Starting graphics benchmark...");
			benchmarker.Start();
			benchmarker.Finished += (avgFps) =>
			{
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
					Set(SharedSettingKeys.Graphics.Preset, lower);
				}
				else if (avgFps >= 55f)
				{
					GraphicsPreset higher = current switch
					{
						GraphicsPreset.Low => GraphicsPreset.Medium,
						GraphicsPreset.Medium => GraphicsPreset.High,
						_ => current
					};
					Set(SharedSettingKeys.Graphics.Preset, higher);
				}
				benchmarker.QueueFree();
			};
		}

		if (world.IsLoaded)
			StartProfile();
		else
			world.Loaded.Connect(StartProfile);
	}

	protected override void OnAfterSet(string key, object normalizedValue)
	{
		GraphicsPresetManager.HandlePresetChange(this, key, normalizedValue);
	}
}
