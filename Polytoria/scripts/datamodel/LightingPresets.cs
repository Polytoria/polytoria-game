// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

internal static class LightingPresets
{
	private const string BasePath = "res://resources/materials/skyboxes/";

	private static readonly Dictionary<LightingPreset, string> _fileNames = new()
	{
		{ LightingPreset.Day1, "Day1" },
		{ LightingPreset.Day2, "Day2" },
		{ LightingPreset.Day3, "Day3" },
		{ LightingPreset.Day4, "Day4" },
		{ LightingPreset.Day5, "Day5" },
		{ LightingPreset.Day6, "Day6" },
		{ LightingPreset.Day7, "Day7" },
		{ LightingPreset.Morning1, "Morning1" },
		{ LightingPreset.Morning2, "Morning2" },
		{ LightingPreset.Morning3, "Morning3" },
		{ LightingPreset.Morning4, "Morning4" },
		{ LightingPreset.Night1, "Night1" },
		{ LightingPreset.Night2, "Night2" },
		{ LightingPreset.Night3, "Night3" },
		{ LightingPreset.Night4, "Night4" },
		{ LightingPreset.Night5, "Night5" },
		{ LightingPreset.Sunset1, "Sunset1" },
		{ LightingPreset.Sunset2, "Sunset2" },
		{ LightingPreset.Sunset3, "Sunset3" },
		{ LightingPreset.Sunset4, "Sunset4" },
		{ LightingPreset.Sunset5, "Sunset5" }
	};

	private static bool ColorApprox(Color a, Color b, float channelTolerance = 1f / 255f) =>
		Mathf.Abs(a.R - b.R) <= channelTolerance &&
		Mathf.Abs(a.G - b.G) <= channelTolerance &&
		Mathf.Abs(a.B - b.B) <= channelTolerance &&
		Mathf.Abs(a.A - b.A) <= channelTolerance;

	private static Color ReadColor(ShaderMaterial mat, string param, Color fallback)
	{
		Variant v = mat.GetShaderParameter(param);
		return v.VariantType == Variant.Type.Nil ? fallback : v.AsColor();
	}

	private static float ReadFloat(ShaderMaterial mat, string param, float fallback)
	{
		Variant v = mat.GetShaderParameter(param);
		return v.VariantType == Variant.Type.Nil ? fallback : v.AsSingle();
	}

	private static bool ParamMatchesColor(ShaderMaterial mat, string param, Color current)
	{
		Variant v = mat.GetShaderParameter(param);
		return v.VariantType == Variant.Type.Nil || ColorApprox(v.AsColor(), current);
	}

	private static bool ParamMatchesFloat(ShaderMaterial mat, string param, float current)
	{
		Variant v = mat.GetShaderParameter(param);
		return v.VariantType == Variant.Type.Nil || Mathf.IsEqualApprox(v.AsSingle(), current);
	}

	public static LightingPreset? FindMatchingPreset(Sky sky)
	{
		if (sky.Skybox is not GradientSkyboxAsset gradient) return null;

		Lighting lighting = sky.Root.Lighting;
		SunLight sun = lighting.Sun;
		MoonLight moon = lighting.Moon;

		foreach (var (preset, name) in _fileNames)
		{
			ShaderMaterial? mat = GD.Load<ShaderMaterial>($"{BasePath}{name}.tres");
			if (mat == null) continue;

			bool match =
				ParamMatchesFloat(mat, "clock_time", lighting.ClockTime) &&
				ParamMatchesColor(mat, "sun_disc_color", sun.Color) &&
				ParamMatchesFloat(mat, "sun_disc_exponent", sun.Size.Length() * 100000f) &&
				ParamMatchesColor(mat, "sun_halo_color", sun.HaloColor) &&
				ParamMatchesFloat(mat, "sun_halo_exponent", sun.HaloExponent) &&
				ParamMatchesFloat(mat, "sun_halo_contribution", sun.HaloContribution) &&
				ParamMatchesColor(mat, "moon_disc_color", moon.Color) &&
				ParamMatchesFloat(mat, "moon_disc_exponent", moon.Size.Length() * 100000f) &&
				ParamMatchesColor(mat, "moon_halo_color", moon.HaloColor) &&
				ParamMatchesFloat(mat, "moon_halo_exponent", moon.HaloExponent) &&
				ParamMatchesFloat(mat, "moon_halo_contribution", moon.HaloContribution) &&
				ParamMatchesColor(mat, "horizon_line_color", gradient.HorizonLineColor) &&
				ParamMatchesFloat(mat, "horizon_line_exponent", gradient.HorizonLineExponent) &&
				ParamMatchesFloat(mat, "horizon_line_contribution", gradient.HorizonLineContribution) &&
				ParamMatchesColor(mat, "sky_gradient_top", gradient.SkyGradientTop) &&
				ParamMatchesColor(mat, "sky_gradient_bottom", gradient.SkyGradientBottom) &&
				ParamMatchesFloat(mat, "sky_gradient_exponent", gradient.SkyGradientExponent);

			if (match) return preset;
		}

		return null;
	}

	public static void Apply(Sky sky, LightingPreset preset)
	{
		if (!_fileNames.TryGetValue(preset, out string name)) return;

		ShaderMaterial? mat = GD.Load<ShaderMaterial>($"{BasePath}{name}.tres");
		if (mat == null) return;

		Lighting lighting = sky.Root.Lighting;
		lighting.ClockTime = ReadFloat(mat, "clock_time", lighting.ClockTime);

		SunLight sun = lighting.Sun;
		sun.Color = ReadColor(mat, "sun_disc_color", sun.Color);
		float sunExponent = ReadFloat(mat, "sun_disc_exponent", sun.Size.Length() * 100000f);
		if (sunExponent > 0f) sun.Size = Vector3.One.Normalized() * (sunExponent / 100000f);
		sun.HaloColor = ReadColor(mat, "sun_halo_color", sun.HaloColor);
		sun.HaloExponent = ReadFloat(mat, "sun_halo_exponent", sun.HaloExponent);
		sun.HaloContribution = ReadFloat(mat, "sun_halo_contribution", sun.HaloContribution);

		MoonLight moon = lighting.Moon;
		moon.Color = ReadColor(mat, "moon_disc_color", moon.Color);
		float moonExponent = ReadFloat(mat, "moon_disc_exponent", moon.Size.Length() * 100000f);
		if (moonExponent > 0f) moon.Size = Vector3.One.Normalized() * (moonExponent / 100000f);
		moon.HaloColor = ReadColor(mat, "moon_halo_color", moon.HaloColor);
		moon.HaloExponent = ReadFloat(mat, "moon_halo_exponent", moon.HaloExponent);
		moon.HaloContribution = ReadFloat(mat, "moon_halo_contribution", moon.HaloContribution);

		GradientSkyboxAsset gradient = sky.Skybox as GradientSkyboxAsset ?? new();
		gradient.HorizonLineColor = ReadColor(mat, "horizon_line_color", gradient.HorizonLineColor);
		gradient.HorizonLineExponent = ReadFloat(mat, "horizon_line_exponent", gradient.HorizonLineExponent);
		gradient.HorizonLineContribution = ReadFloat(mat, "horizon_line_contribution", gradient.HorizonLineContribution);
		gradient.SkyGradientTop = ReadColor(mat, "sky_gradient_top", gradient.SkyGradientTop);
		gradient.SkyGradientBottom = ReadColor(mat, "sky_gradient_bottom", gradient.SkyGradientBottom);
		gradient.SkyGradientExponent = ReadFloat(mat, "sky_gradient_exponent", gradient.SkyGradientExponent);
		sky.Skybox = gradient;
	}
}

[ScriptEnum]
public enum LightingPreset
{
	Custom,
	Day1, Day2, Day3, Day4, Day5, Day6, Day7,
	Morning1, Morning2, Morning3, Morning4,
	Night1, Night2, Night3, Night4, Night5,
	Sunset1, Sunset2, Sunset3, Sunset4, Sunset5
}
