// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Shared.Settings;

public static class SharedSettingKeys
{
	public static class Display
	{
		public const string Fullscreen = "display.fullscreen";
		public const string VSync = "display.vsync";
	}

	public static class Graphics
	{
		public const string Prefix = "graphics.";
		public const string RenderingMethod = "graphics.rendering_method";
		public const string Preset = "graphics.preset";
		public const string RenderScale = "graphics.render_scale";
		public const string Msaa = "graphics.msaa";
		public const string ShadowQuality = "graphics.shadow_quality";
		public const string ShadowDistance = "graphics.shadow_distance";
	}

	public static class PostProcessing
	{
		public const string Prefix = "graphics.post_processing.";
		public const string Glow = "graphics.post_processing.glow";
		public const string Ssao = "graphics.post_processing.ssao";
		public const string Ssr = "graphics.post_processing.ssr";
		public const string Ssil = "graphics.post_processing.ssil";
		public const string Sdfgi = "graphics.post_processing.sdfgi";
		public const string SdfgiCellSize = "graphics.post_processing.sdfgi_cell_size";
		public const string SdfgiCascades = "graphics.post_processing.sdfgi_cascades";
		public const string SsilRadius = "graphics.post_processing.ssil_radius";
		public const string NormalMaps = "graphics.post_processing.normal_maps";
	}
}
