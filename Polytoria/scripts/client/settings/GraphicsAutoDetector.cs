// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Linq;

using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings;

public static class GraphicsAutoDetector
{
	public static GraphicsPreset Detect()
	{
		int cores = OS.GetProcessorCount();
		string gpu = string.Join(" ", OS.GetVideoAdapterDriverInfo());
		bool isMobile = Globals.IsMobileBuild;

		int score = 0;

		if (cores <= 2) score -= 2;
		else if (cores <= 4) score -= 1;
		else if (cores >= 8) score += 1;

		if (isMobile) score -= 2;

		if (ContainsAny(gpu, "intel hd", "intel uhd", "vega 3", "vega 8")) score -= 3;
		else if (ContainsAny(gpu, "iris xe", "radeon graphics", "apple m1", "apple m2")) score -= 1;
		else if (ContainsAny(gpu, "rtx", "rx 6", "rx 7", "arc a", "apple m3", "apple m4")) score += 2;

		return score switch
		{
			<= -3 => GraphicsPreset.Low,
			<= 0 => GraphicsPreset.Medium,
			<= 2 => GraphicsPreset.High,
			_ => GraphicsPreset.Ultra
		};
	}

	private static bool ContainsAny(string text, params string[] values)
	{
		return values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
	}
}
