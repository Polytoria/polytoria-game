using Polytoria.Shared.Settings;
using System.Collections.Generic;

namespace Polytoria.Client.Settings;

public static class ClientSettingsRegistry
{
	public static readonly IReadOnlyList<SettingSectionDef> Sections =
	[
		new() {Key = "general", Label = "General", IconPath = "res://assets/textures/ui-icons/settings.svg", SortOrder = 0},
		new() {Key = "display", Label = "Display", IconPath = "res://assets/textures/ui-icons/camera.svg", SortOrder = 1},
		new() {Key = "graphics", Label = "Graphics", IconPath = "res://assets/textures/ui-icons/mountain.svg", SortOrder = 2},
		new() {Key = "post_processing", Label = "Post Processing", IconPath = "res://assets/textures/ui-icons/rocket.svg", SortOrder = 3},
		new() {Key = "overlay", Label = "Overlay", IconPath = "res://assets/textures/ui-icons/copy.svg", SortOrder = 4},
		new() {Key = "advanced", Label = "Advanced", IconPath = "res://assets/textures/ui-icons/code.svg", SortOrder = 5}
	];

	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>
		{
			{
				ClientSettingKeys.General.CtrlLock,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.General.CtrlLock,
					SectionKey = "general",
					Label = "Ctrl Lock",
					Description = "Allow Ctrl Lock while in third person.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				ClientSettingKeys.Display.Fullscreen,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.Display.Fullscreen,
					SectionKey = "display",
					Label = "Fullscreen",
					Description = "Use fullscreen window mode.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = false
				}
			},
			{
				ClientSettingKeys.General.MasterVolume,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.General.MasterVolume,
					SectionKey = "general",
					Label = "Volume",
					Description = "Master game volume.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 80f,
					MinValue = 0f,
					MaxValue = 100f,
					Step = 1f
				}
			},
			{
				ClientSettingKeys.General.CameraSensitivity,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.General.CameraSensitivity,
					SectionKey = "general",
					Label = "Camera Sensitivity",
					Description = "Camera movement sensitivity.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Slider,
					DefaultValue = 0.6f,
					MinValue = 0.2f,
					MaxValue = 1.2f,
					Step = 0.1f
				}
			},
			{
				ClientSettingKeys.Display.UiScale,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.Display.UiScale,
					SectionKey = "display",
					Label = "UI Scale",
					Description = "Scale of the user interface.",
					ValueKind = SettingValueKind.Float,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = 1f,
					Options =
					[
						new() { Value = 0.5f, Label = "0.5x" },
						new() { Value = 0.75f, Label = "0.75x" },
						new() { Value = 1f, Label = "1x" },
						new() { Value = 1.25f, Label = "1.25x" },
						new() { Value = 1.5f, Label = "1.5x" },
						new() { Value = 1.75f, Label = "1.75x" },
						new() { Value = 2f, Label = "2x" },
					]
				}
			},
			{
				ClientSettingKeys.Overlay.PerformanceOverlayMode,
				new SettingDef<OverlayMode>
				{
					Key = ClientSettingKeys.Overlay.PerformanceOverlayMode,
					SectionKey = "overlay",
					Label = "Performance Overlay",
					Description = "Show performance information on the screen.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = OverlayMode.None,
					Options =
					[
						new() { Value = OverlayMode.None, Label = "None" },
						new() { Value = OverlayMode.Minimal, Label = "Minimal" },
						new() { Value = OverlayMode.Full, Label = "Full" },
					]
				}
			},
			{
				ClientSettingKeys.Overlay.ConnectionIndicators,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.Overlay.ConnectionIndicators,
					SectionKey = "overlay",
					Label = "Show Connection Indicators",
					Description = "Show connection status warnings.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				ClientSettingKeys.Graphics.Preset,
				new SettingDef<GraphicsPreset>
				{
					Key = ClientSettingKeys.Graphics.Preset,
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
				ClientSettingKeys.Graphics.RenderingMethod,
				new SettingDef<RenderingMethodOption>
				{
					Key = ClientSettingKeys.Graphics.RenderingMethod,
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
				ClientSettingKeys.Display.VSync,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.Display.VSync,
					SectionKey = "display",
					Label = "V-Sync",
					Description = "Synchronize frames to display refresh.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				ClientSettingKeys.Graphics.RenderScale,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.Graphics.RenderScale,
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
				ClientSettingKeys.Graphics.Msaa,
				new SettingDef<MsaaOption>
				{
					Key = ClientSettingKeys.Graphics.Msaa,
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
				ClientSettingKeys.Graphics.ShadowQuality,
				new SettingDef<ShadowQuality>
				{
					Key = ClientSettingKeys.Graphics.ShadowQuality,
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
				ClientSettingKeys.Graphics.ShadowDistance,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.Graphics.ShadowDistance,
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
				ClientSettingKeys.PostProcessing.Glow,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.Glow,
					SectionKey = "post_processing",
					Label = "Glow",
					Description = "Toggle glow/bloom effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.PostProcessing.Ssao,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.Ssao,
					SectionKey = "post_processing",
					Label = "SSAO",
					Description = "Toggle ambient occlusion effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.PostProcessing.Ssr,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.Ssr,
					SectionKey = "post_processing",
					Label = "SSR",
					Description = "Toggle screen-space reflections.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.PostProcessing.Ssil,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.Ssil,
					SectionKey = "post_processing",
					Label = "SSIL",
					Description = "Toggle screen-space illuminated lighting.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.PostProcessing.Sdfgi,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.Sdfgi,
					SectionKey = "post_processing",
					Label = "SDFGI",
					Description = "Toggle SDFGI (semi-real-time global illumination) effect.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.PostProcessing.NormalMaps,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.PostProcessing.NormalMaps,
					SectionKey = "post_processing",
					Label = "Normal Maps",
					Description = "Toggle normal maps on part materials.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			{
				ClientSettingKeys.Advanced.ShowAdvancedSettings,
				new SettingDef<bool>
				{
					Key = ClientSettingKeys.Advanced.ShowAdvancedSettings,
					SectionKey = "advanced",
					Label = "Show Advanced Settings",
					Description = "Shows hidden advanced settings.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true,
				}
			},
			// Put advanced settings under here
			{
				ClientSettingKeys.PostProcessing.SdfgiCellSize,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.PostProcessing.SdfgiCellSize,
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
				ClientSettingKeys.PostProcessing.SdfgiCascades,
				new SettingDef<int>
				{
					Key = ClientSettingKeys.PostProcessing.SdfgiCascades,
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
				ClientSettingKeys.PostProcessing.SsilRadius,
				new SettingDef<float>
				{
					Key = ClientSettingKeys.PostProcessing.SsilRadius,
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
			}
		};

		return defs;
	}
}
