namespace Polytoria.Client.Settings;

public static class ClientSettingKeys
{
	public static class General
	{
		public const string CtrlLock = "general.ctrl_lock";
		public const string MasterVolume = "general.master_volume";
		public const string CameraSensitivity = "general.camera_sensitivity";
	}

	public static class Display
	{
		public const string Fullscreen = "display.fullscreen";
		public const string UiScale = "display.ui_scale";
		public const string VSync = "display.vsync";
	}

	public static class Overlay
	{
		public const string PerformanceOverlayMode = "overlay.performance_mode";
		public const string ConnectionIndicators = "overlay.connection_indicators";
	}

	public static class Graphics
	{
		public const string Preset = "graphics.preset";
		public const string RenderingMethod = "graphics.rendering_method";
		public const string RenderScale = "graphics.render_scale";
		public const string Msaa = "graphics.msaa";
		public const string ShadowQuality = "graphics.shadow_quality";
		public const string ShadowDistance = "graphics.shadow_distance";
	}

	public static class PostProcessing
	{
		public const string Glow = "graphics.post_processing.glow";
		public const string Ssao = "graphics.post_processing.ssao";
		public const string Ssr = "graphics.post_processing.ssr";
		public const string Ssil = "graphics.post_processing.ssil";
		public const string Sdfgi = "graphics.post_processing.sdfgi";
		public const string NormalMaps = "graphics.post_processing.normal_maps";

		public const string SdfgiCellSize = "graphics.post_processing.sdfgi_cell_size";
		public const string SdfgiCascades = "graphics.post_processing.sdfgi_cascades";
		public const string SsilRadius = "graphics.post_processing.ssil_radius";
	}

	public static class Advanced
	{
		public const string ShowAdvancedSettings = "advanced.show_advanced_settings";
	}
}
