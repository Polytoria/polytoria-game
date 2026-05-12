// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared.Settings;
using System;

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
			_ => RenderingDeviceEnum.Forward
		};
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
		OS.CreateProcess(exePath, [.. args, "--rendering-method", renderingName, "-rmswignore"]);
		Globals.Singleton.Quit(force: true);
		throw new SwitchingRenderingDeviceException();
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
