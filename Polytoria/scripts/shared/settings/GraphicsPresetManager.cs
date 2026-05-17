// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Shared.Settings;

public static class GraphicsPresetManager
{
	private static readonly HashSet<string> PresetManagedKeys = new()
	{
		SharedSettingKeys.Graphics.RenderScale,
		SharedSettingKeys.Graphics.Msaa,
		SharedSettingKeys.Graphics.ShadowQuality,
		SharedSettingKeys.Graphics.ShadowDistance,
		SharedSettingKeys.PostProcessing.Glow,
		SharedSettingKeys.PostProcessing.Ssao,
		SharedSettingKeys.PostProcessing.Ssr,
		SharedSettingKeys.PostProcessing.Ssil,
		SharedSettingKeys.PostProcessing.Sdfgi,
		SharedSettingKeys.PostProcessing.NormalMaps,
	};

	public static bool IsPresetManagedKey(string key)
	{
		return key == SharedSettingKeys.Graphics.RenderingMethod || PresetManagedKeys.Contains(key);
	}

	private sealed record PresetData(
		float RenderScale,
		MsaaOption Msaa,
		ShadowQuality ShadowQuality,
		float ShadowDistance,
		bool Glow,
		bool Ssao,
		bool Ssr,
		bool Ssil,
		bool Sdfgi,
		bool NormalMaps
	)
	{
		public void ApplyTo(ISettingsContext settings)
		{
			settings.Set(SharedSettingKeys.Graphics.RenderScale, RenderScale);
			settings.Set(SharedSettingKeys.Graphics.Msaa, Msaa);
			settings.Set(SharedSettingKeys.Graphics.ShadowQuality, ShadowQuality);
			settings.Set(SharedSettingKeys.Graphics.ShadowDistance, ShadowDistance);
			settings.Set(SharedSettingKeys.PostProcessing.Glow, Glow);
			settings.Set(SharedSettingKeys.PostProcessing.Ssao, Ssao);
			settings.Set(SharedSettingKeys.PostProcessing.Ssr, Ssr);
			settings.Set(SharedSettingKeys.PostProcessing.Ssil, Ssil);
			settings.Set(SharedSettingKeys.PostProcessing.Sdfgi, Sdfgi);
			settings.Set(SharedSettingKeys.PostProcessing.NormalMaps, NormalMaps);
		}
	}

	private static readonly Dictionary<GraphicsPreset, PresetData> Presets = new()
	{
		[GraphicsPreset.Low] = new(
			RenderScale: 0.75f,
			Msaa: MsaaOption.Disabled,
			ShadowQuality: ShadowQuality.Off,
			ShadowDistance: 100f,
			Glow: false,
			Ssao: false,
			Ssr: false,
			Ssil: false,
			Sdfgi: false,
			NormalMaps: false
		),
		[GraphicsPreset.Medium] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X2,
			ShadowQuality: ShadowQuality.Medium,
			ShadowDistance: 1000f,
			Glow: true,
			Ssao: true,
			Ssr: false,
			Ssil: false,
			Sdfgi: false,
			NormalMaps: true
		),
		[GraphicsPreset.High] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X4,
			ShadowQuality: ShadowQuality.High,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: false,
			Sdfgi: false,
			NormalMaps: true
		),
		[GraphicsPreset.Ultra] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X8,
			ShadowQuality: ShadowQuality.Ultra,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: true,
			Sdfgi: false,
			NormalMaps: true
		),
		[GraphicsPreset.Photo] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X8,
			ShadowQuality: ShadowQuality.Ultra,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: true,
			Sdfgi: true,
			NormalMaps: true
		),
	};

	public static void ApplyPreset(ISettingsContext settings, GraphicsPreset preset)
	{
		if (!Presets.TryGetValue(preset, out var data))
		{
			PT.PrintErr($"GraphicsPresetManager: Unknown preset '{preset}', no changes applied.");
			return;
		}

		data.ApplyTo(settings);
	}

	private static int _presetDepth;

	public static void HandlePresetChange(ISettingsContext settings, string key, object normalizedValue)
	{
		if (!key.StartsWith(SharedSettingKeys.Graphics.Prefix))
			return;

		if (key == SharedSettingKeys.Graphics.Preset)
		{
			GraphicsPreset preset = (GraphicsPreset)normalizedValue;
			if (preset == GraphicsPreset.Custom)
				return;

			_presetDepth++;
			try
			{
				ApplyPreset(settings, preset);
			}
			finally
			{
				_presetDepth--;
			}
			return;
		}

		if (_presetDepth > 0 || !IsPresetManagedKey(key))
			return;

		GraphicsPreset currentPreset = settings.Get<GraphicsPreset>(SharedSettingKeys.Graphics.Preset);
		if (currentPreset != GraphicsPreset.Custom)
		{
			_presetDepth++;
			try
			{
				settings.Set(SharedSettingKeys.Graphics.Preset, GraphicsPreset.Custom);
			}
			finally
			{
				_presetDepth--;
			}
		}
	}
}
