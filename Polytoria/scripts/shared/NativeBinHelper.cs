// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using Godot;
using Polytoria.Utils;
using System;
using System.Runtime.InteropServices;

namespace Polytoria.Shared;

public static partial class NativeBinHelper
{
	public const string LuaLSEditorExecutablePath = "res://native/luau-lsp/";
	public const string StyLuaEditorExecutablePath = "res://native/stylua/";

	[LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	private static partial int chmod(string pathname, int mode);

	public static void Init()
	{
#if CREATOR && GODOT_LINUXBSD
		InitLinuxCreator();
#elif CREATOR && GODOT_MACOS
		InitMacOSCreator();
#endif
	}

	private static void SetExecutablePermission(string path, string label)
	{
		int ret = chmod(path, 0x755);
		if (ret != 0)
			throw new Exception($"{label} permission set failure: Code {ret}");
	}

	private static void InitLinuxCreator()
	{
#if CREATOR
		SetExecutablePermission(ResolveLuauLspBinPath(), "Linux Luau LSP");
		SetExecutablePermission(ResolveStyLuaBinPath(), "Linux StyLua");

#endif
	}

	private static void InitMacOSCreator()
	{
#if CREATOR
		SetExecutablePermission(ResolveLuauLspBinPath(), "macOS Luau LSP");
		SetExecutablePermission(ResolveStyLuaBinPath(), "macOS StyLua");
#endif
	}

	internal static string ResolveLuauLspBinPath()
	{
		string basePath;
		string? exeName = null;

		if (Globals.IsInGDEditor)
		{
			basePath = LuaLSEditorExecutablePath;
		}
		else
		{
			basePath = OS.GetExecutablePath().GetBaseDir();
		}

		if (OS.HasFeature("windows"))
		{
			exeName = "luau-lsp.exe";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("windows");
		}
		else if (OS.HasFeature("macos"))
		{
			exeName = "luau-lsp";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("macos");
		}
		else if (OS.HasFeature("linux"))
		{
			exeName = "luau-lsp";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("linux");
		}

		if (exeName == null) throw new Exception("Unsupported platform for luau-lsp");

		string exePath = basePath.PathJoin(exeName);
		string exePathGlobal = ProjectSettings.GlobalizePath(exePath).SanitizePath();
		return exePathGlobal;
	}

	internal static string ResolveStyLuaBinPath()
	{
		string basePath;
		string? exeName = null;

		if (Globals.IsInGDEditor)
		{
			basePath = StyLuaEditorExecutablePath;
		}
		else
		{
			basePath = OS.GetExecutablePath().GetBaseDir();
		}

		if (OS.HasFeature("windows"))
		{
			exeName = "stylua.exe";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("windows");
		}
		else if (OS.HasFeature("macos"))
		{
			exeName = "stylua";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("macos");
		}
		else if (OS.HasFeature("linux"))
		{
			exeName = "stylua";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("linux");
		}

		if (exeName == null) throw new Exception("Unsupported platform for stylua");

		string exePath = basePath.PathJoin(exeName);
		string exePathGlobal = ProjectSettings.GlobalizePath(exePath).SanitizePath();
		return exePathGlobal;
	}
}
