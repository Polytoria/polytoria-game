// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;

namespace Polytoria.Shared;

public static class RenderingDeviceSwitcher
{
	public static RenderingDeviceEnum FromRenderingMethodOption(RenderingMethodOption option)
	{
		return option switch
		{
			RenderingMethodOption.Standard => RenderingDeviceEnum.Forward,
			RenderingMethodOption.Performance => RenderingDeviceEnum.Mobile,
			RenderingMethodOption.Compatibility => RenderingDeviceEnum.GLCompatibility,
			RenderingMethodOption.Auto => throw new ArgumentException("Auto does not map to rendering device"),
			_ => RenderingDeviceEnum.Forward
		};
	}

	public static void Switch(RenderingMethodOption option)
	{
		if (option == RenderingMethodOption.Auto)
		{
			return;
		}

		Switch(FromRenderingMethodOption(option));
	}

	public static void Switch(RenderingDeviceEnum to)
	{
		// Mobile are locked to one renderer only, don't change
		if (Globals.IsMobileBuild) return;

		string renderingName = GetRenderingName(to);
		string currentMethod = RenderingServer.GetCurrentRenderingMethod();
		if (currentMethod == renderingName)
		{
			// already using this rendering, nothing to do
			return;
		}

		string[] args = OS.GetCmdlineArgs();

		if (args.Contains("-rmswignore"))
		{
			// Already switched, but godot may have refused it. let's just go with that anyways
			return;
		}

		string exePath = OS.GetExecutablePath();

		// rebuild command line arguments, replaces existing rendering method argument with the new one
		OS.CreateProcess(exePath, GetRestartArgs(args, renderingName));

		Globals.Singleton.Quit(force: true);
		throw new SwitchingRenderingDeviceException();
	}

	private static string[] GetRestartArgs(string[] args, string renderingName)
	{
		List<string> filtered = new List<string>();

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];

			// skip existing rendering method arguments
			if (arg == "--rendering-method" || arg == "--rendering-driver")
			{
				if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
					i++;

				continue;
			}

			if (arg == "-rmswignore")
				continue;

			filtered.Add(arg);
		}

		filtered.Add("--rendering-method");
		filtered.Add(renderingName);
		filtered.Add("-rmswignore");

		return filtered.ToArray();
	}

	public static string GetCurrentDriverName()
	{
		return RenderingServer.GetCurrentRenderingMethod();
	}

	public static string GetRenderingName(RenderingDeviceEnum e)
	{
		return e switch
		{
			RenderingDeviceEnum.Forward => "forward_plus",
			RenderingDeviceEnum.Mobile => "mobile",
			RenderingDeviceEnum.GLCompatibility => "gl_compatibility",
			_ => throw new IndexOutOfRangeException()
		};
	}

	public class SwitchingRenderingDeviceException : Exception { }

	public enum RenderingDeviceEnum
	{
		Forward,
		Mobile,
		GLCompatibility
	}
}
