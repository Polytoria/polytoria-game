// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Collections.Generic;

namespace Polytoria.Shared.Settings;

public static class SharedSettingsRegistry
{
	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	public static void AddSharedTo(Dictionary<string, SettingDef> target)
	{
		foreach (var pair in Definitions)
			target[pair.Key] = pair.Value;
	}

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>
		{
			{
				SharedSettingKeys.Display.Fullscreen,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.Display.Fullscreen,
					SectionKey = "display",
					Label = "Fullscreen",
					Description = "Use fullscreen window mode.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = false
				}
			},
			{
				SharedSettingKeys.Display.VSync,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.Display.VSync,
					SectionKey = "display",
					Label = "V-Sync",
					Description = "Synchronize frames to display refresh.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.Graphics.Preset,
				new SettingDef<GraphicsPreset>
				{
					Key = SharedSettingKeys.Graphics.Preset,
					SectionKey = "graphics",
					Label = "Graphics Preset",
					Description = "Overall graphics quality preset.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = GraphicsPreset.Medium,
					Options =
					[
						new() { Value = GraphicsPreset.Low, Label = "Low" },
						new() { Value = GraphicsPreset.Medium, Label = "Medium" },
						new() { Value = GraphicsPreset.High, Label = "High" },
						new() { Value = GraphicsPreset.Ultra, Label = "Ultra" },
						new() { Value = GraphicsPreset.Photo, Label = "Photo" },
						new() { Value = GraphicsPreset.Custom, Label = "Custom" },
					]
				}
			},
			{
				SharedSettingKeys.Graphics.RenderingMethod,
				new SettingDef<RenderingMethodOption>
				{
					Key = SharedSettingKeys.Graphics.RenderingMethod,
					SectionKey = "graphics",
					Label = "Rendering Method",
					Description = "Rendering method to use. Use compatibility on older hardware.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = RenderingMethodOption.Standard,
					RequiresRestart = true,
					Options =
					[
						new() { Value = RenderingMethodOption.Standard, Label = "Standard" },
						new() { Value = RenderingMethodOption.Performance, Label = "Performance" },
						new() { Value = RenderingMethodOption.Compatibility, Label = "Compatibility" },
					]
				}
			},
			{
				SharedSettingKeys.Graphics.RenderScale,
				new SettingDef<float>
				{
					Key = SharedSettingKeys.Graphics.RenderScale,
					SectionKey = "graphics",
					Label = "Render Scale",
					Description = "The resolution scale to render graphics at.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 1.0f,
					MinValue = 0.2f,
					MaxValue = 1.0f,
					Step = 0.05f
				}
			},
			{
				SharedSettingKeys.Graphics.Msaa,
				new SettingDef<MsaaOption>
				{
					Key = SharedSettingKeys.Graphics.Msaa,
					SectionKey = "graphics",
					Label = "MSAA Level",
					Description = "MSAA anti-aliasing level.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = MsaaOption.X2,
					Options =
					[
						new() { Value = MsaaOption.Disabled, Label = "Off" },
						new() { Value = MsaaOption.X2, Label = "2x" },
						new() { Value = MsaaOption.X4, Label = "4x" },
						new() { Value = MsaaOption.X8, Label = "8x" },
					]
				}
			},
			{
				SharedSettingKeys.Graphics.ShadowQuality,
				new SettingDef<ShadowQuality>
				{
					Key = SharedSettingKeys.Graphics.ShadowQuality,
					SectionKey = "graphics",
					Label = "Shadow Quality",
					Description = "Shadow quality level.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = ShadowQuality.Medium,
					Options =
					[
						new() { Value = ShadowQuality.Off, Label = "Off" },
						new() { Value = ShadowQuality.Low, Label = "Low" },
						new() { Value = ShadowQuality.Medium, Label = "Medium" },
						new() { Value = ShadowQuality.High, Label = "High" },
						new() { Value = ShadowQuality.Ultra, Label = "Ultra" },
					]
				}
			},
			{
				SharedSettingKeys.Graphics.ShadowDistance,
				new SettingDef<float>
				{
					Key = SharedSettingKeys.Graphics.ShadowDistance,
					SectionKey = "graphics",
					Label = "Shadow Distance",
					Description = "How far shadows are visible.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 1000f,
					MinValue = 5f,
					MaxValue = 1250f,
					Step = 5f
				}
			},
			{
				SharedSettingKeys.PostProcessing.Glow,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.Glow,
					SectionKey = "post_processing",
					Label = "Glow",
					Description = "Toggle glow/bloom effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.Ssao,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.Ssao,
					SectionKey = "post_processing",
					Label = "SSAO",
					Description = "Toggle ambient occlusion effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.Ssr,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.Ssr,
					SectionKey = "post_processing",
					Label = "SSR",
					Description = "Toggle screen-space reflections.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.Ssil,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.Ssil,
					SectionKey = "post_processing",
					Label = "SSIL",
					Description = "Toggle screen-space illuminated lighting.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.Sdfgi,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.Sdfgi,
					SectionKey = "post_processing",
					Label = "SDFGI",
					Description = "Toggle SDFGI (semi-real-time global illumination) effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.NormalMaps,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.PostProcessing.NormalMaps,
					SectionKey = "post_processing",
					Label = "Normal Maps",
					Description = "Toggle normal maps on part materials.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.PostProcessing.SdfgiCellSize,
				new SettingDef<float>
				{
					Key = SharedSettingKeys.PostProcessing.SdfgiCellSize,
					IsAdvanced = true,
					SectionKey = "post_processing",
					Label = "SDFGI Cell Size",
					Description = "Size of SDFGI cells. Larger cells improve performance but reduce quality.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 0.8f,
					MinValue = 0.2f,
					MaxValue = 2f,
					Step = 0.1f
				}
			},
			{
				SharedSettingKeys.PostProcessing.SdfgiCascades,
				new SettingDef<int>
				{
					Key = SharedSettingKeys.PostProcessing.SdfgiCascades,
					IsAdvanced = true,
					SectionKey = "post_processing",
					Label = "SDFGI Cascades",
					Description = "Number of cascades for SDFGI.",
					ValueKind = SettingValueKind.Int,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 6,
					MinValue = 1,
					MaxValue = 8,
					Step = 1
				}
			},
			{
				SharedSettingKeys.PostProcessing.SsilRadius,
				new SettingDef<float>
				{
					Key = SharedSettingKeys.PostProcessing.SsilRadius,
					IsAdvanced = true,
					SectionKey = "post_processing",
					Label = "SSIL Radius",
					Description = "Radius for SSIL effect",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 10f,
					MinValue = 1f,
					MaxValue = 50f,
					Step = 1f
				}
			},
		};

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
