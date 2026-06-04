// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.LSP.Schemas;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Creator.LSP;

public static class LuaFormatService
{
	public static async Task<string> FormatScriptAsync(string scriptPath, string scriptText)
	{
		try
		{
			var process = Process.Start(new ProcessStartInfo
			{
				FileName = NativeBinHelper.ResolveStyLuaBinPath(),
				Arguments = $"--stdin-filepath \"{scriptPath}\" -",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			})!;

			await process.StandardInput.WriteAsync(scriptText);
			process.StandardInput.Close();

			string formatted = await process.StandardOutput.ReadToEndAsync();
			await process.WaitForExitAsync();

			if (process.ExitCode != 0)
			{
				string err = await process.StandardError.ReadToEndAsync();
				PT.PrintErr($"StyLua failed: {err}");
				return scriptText;
			}

			return formatted;
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Formatting failed: {ex.Message}");
			return scriptText;
		}
	}
}
