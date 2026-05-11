namespace Polytoria.Client.Settings;

public static class GraphicsPresetManager
{
	public static bool IsPresetManagedKey(string key)
	{
		return key == ClientSettingKeys.Graphics.RenderingMethod
			|| key == ClientSettingKeys.Graphics.RenderScale
			|| key == ClientSettingKeys.Graphics.Msaa
			|| key == ClientSettingKeys.Graphics.ShadowQuality
			|| key == ClientSettingKeys.Graphics.ShadowDistance
			|| key == ClientSettingKeys.PostProcessing.Glow
			|| key == ClientSettingKeys.PostProcessing.Ssao
			|| key == ClientSettingKeys.PostProcessing.Ssr
			|| key == ClientSettingKeys.PostProcessing.Ssil
			|| key == ClientSettingKeys.PostProcessing.Sdfgi
			|| key == ClientSettingKeys.PostProcessing.NormalMaps;
	}

	public static void ApplyPreset(GraphicsPreset preset)
	{
		var settings = ClientSettingsService.Instance;

		switch (preset)
		{
			case GraphicsPreset.Low:
				settings.Set(ClientSettingKeys.Graphics.RenderScale, 0.75f);
				settings.Set(ClientSettingKeys.Graphics.Msaa, MsaaOption.Disabled);
				settings.Set(ClientSettingKeys.Graphics.ShadowQuality, ShadowQuality.Off);
				settings.Set(ClientSettingKeys.Graphics.ShadowDistance, 100f);
				settings.Set(ClientSettingKeys.PostProcessing.Glow, false);
				settings.Set(ClientSettingKeys.PostProcessing.Ssao, false);
				settings.Set(ClientSettingKeys.PostProcessing.Ssr, false);
				settings.Set(ClientSettingKeys.PostProcessing.Ssil, false);
				settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, false);
				settings.Set(ClientSettingKeys.PostProcessing.NormalMaps, false);
				break;
			case GraphicsPreset.Medium:
				settings.Set(ClientSettingKeys.Graphics.RenderScale, 1.0f);
				settings.Set(ClientSettingKeys.Graphics.Msaa, MsaaOption.X2);
				settings.Set(ClientSettingKeys.Graphics.ShadowQuality, ShadowQuality.Medium);
				settings.Set(ClientSettingKeys.Graphics.ShadowDistance, 1000f);
				settings.Set(ClientSettingKeys.PostProcessing.Glow, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssao, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssr, false);
				settings.Set(ClientSettingKeys.PostProcessing.Ssil, false);
				settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, false);
				settings.Set(ClientSettingKeys.PostProcessing.NormalMaps, true);
				break;
			case GraphicsPreset.High:
				settings.Set(ClientSettingKeys.Graphics.RenderScale, 1.0f);
				settings.Set(ClientSettingKeys.Graphics.Msaa, MsaaOption.X4);
				settings.Set(ClientSettingKeys.Graphics.ShadowQuality, ShadowQuality.High);
				settings.Set(ClientSettingKeys.Graphics.ShadowDistance, 1250f);
				settings.Set(ClientSettingKeys.PostProcessing.Glow, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssao, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssr, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssil, false);
				settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, false);
				settings.Set(ClientSettingKeys.PostProcessing.NormalMaps, true);
				break;
			case GraphicsPreset.Ultra:
				settings.Set(ClientSettingKeys.Graphics.RenderScale, 1.0f);
				settings.Set(ClientSettingKeys.Graphics.Msaa, MsaaOption.X8);
				settings.Set(ClientSettingKeys.Graphics.ShadowQuality, ShadowQuality.Ultra);
				settings.Set(ClientSettingKeys.Graphics.ShadowDistance, 1250f);
				settings.Set(ClientSettingKeys.PostProcessing.Glow, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssao, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssr, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssil, true);
				settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, false);
				settings.Set(ClientSettingKeys.PostProcessing.NormalMaps, true);
				break;
			case GraphicsPreset.Photo:
				settings.Set(ClientSettingKeys.Graphics.RenderScale, 1.0f);
				settings.Set(ClientSettingKeys.Graphics.Msaa, MsaaOption.X8);
				settings.Set(ClientSettingKeys.Graphics.ShadowQuality, ShadowQuality.Ultra);
				settings.Set(ClientSettingKeys.Graphics.ShadowDistance, 1250f);
				settings.Set(ClientSettingKeys.PostProcessing.Glow, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssao, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssr, true);
				settings.Set(ClientSettingKeys.PostProcessing.Ssil, true);
				settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, true);
				settings.Set(ClientSettingKeys.PostProcessing.NormalMaps, true);
				break;
			case GraphicsPreset.Custom:
			default:
				break;
		}
	}
}
