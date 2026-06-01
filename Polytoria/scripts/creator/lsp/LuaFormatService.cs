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

public class LuaFormatService(CreatorSession session)
{
	private readonly string _workspacePath = session.ProjectFolderPath;
	private Process _styLuaProcess = null!;

	private StyLuaClient _client = null!;

	public async Task InitAsync()
	{
		ProcessStartInfo processStartInfo = new()
		{
			FileName = NativeBinHelper.ResolveStyLuaBinPath(),
			Arguments = "--lsp",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		_styLuaProcess = Process.Start(processStartInfo) ?? throw new Exception("Failed to start stylua language server process");

		_styLuaProcess.ErrorDataReceived += (sender, e) =>
		{
			if (!string.IsNullOrEmpty(e.Data))
			{
				PT.PrintErr($"StyLua Server Error: {e.Data}");
			}
		};

		_styLuaProcess.BeginErrorReadLine();

		PT.Print("StyLuaLS Started");

		_client = new(_styLuaProcess.StandardOutput.BaseStream, _styLuaProcess.StandardInput.BaseStream);
		await _client.InitializeAsync(_workspacePath);

		PT.Print("StyLua Language server initialized at ", _workspacePath);
	}

	public async Task<string> FormatScriptAsync(string scriptPath, string scriptText)
	{
		// instead of making use of hard disk script text, we use Text from CodeEdit incase if script has not been saved yet
		return await _client.FormatScript(scriptPath, "luau", scriptText);
	}

	public void Shutdown()
	{
		_client?.Dispose();

		if (_styLuaProcess != null && !_styLuaProcess.HasExited)
		{
			_styLuaProcess.Kill();
			_styLuaProcess.Dispose();
		}
	}
}
