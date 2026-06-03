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
	private Process? _styLuaProcess = null;

	private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

	private const string FormatterConfigFile = "stylua.toml";
	private CancellationTokenSource? _restartDebounceCts;

	private StyLuaClient? _client;

	private FileSystemWatcher? _fileSystemWatcher;

	public async Task InitAsync()
	{
		string configFilePath = Path.Join(_workspacePath, FormatterConfigFile);
		bool configFilePresent = File.Exists(configFilePath);

		_fileSystemWatcher = new(_workspacePath)
		{
			Filter = Path.GetFileName(configFilePath)
		};

		_fileSystemWatcher.Created += OnConfigChange;
		_fileSystemWatcher.Deleted += OnConfigChange;
		_fileSystemWatcher.Changed += OnConfigChange;
		_fileSystemWatcher.EnableRaisingEvents = true;


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
		await _client.InitializeAsync(_workspacePath, configFilePresent);

		PT.Print("StyLua Language server initialized at ", _workspacePath);
	}

	public async Task<string> FormatScriptAsync(string scriptPath, string scriptText)
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			if (_client == null)
			{
				PT.PrintErr("StyLua client is not available. Returning original text.");
				return scriptText;
			}

			// instead of making use of hard disk script text, we use Text from CodeEdit incase if script has not been saved yet
			return await _client.FormatScript(scriptPath, "luau", scriptText);
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Formatting failed: {ex.Message}");
			return scriptText;
		}
		finally
		{
			_lifecycleLock.Release();
		}

	}

	private void OnConfigChange(object sender, FileSystemEventArgs e)
	{
		_restartDebounceCts?.Cancel();
		_restartDebounceCts?.Dispose();

		var cts = new CancellationTokenSource();
		_restartDebounceCts = cts;
		var token = cts.Token;

		// need to restart the stylua client
		PT.Print("StyLua config change detected, restarting.");

		_ = DebouncedRestartAsync(token);
	}

	private async Task DebouncedRestartAsync(CancellationToken token)
	{
		try
		{
			await Task.Delay(400, token);
			await RestartAsync();
		}
		catch (OperationCanceledException)
		{
			// Another config change happened
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Debounced restart failed: {ex.Message}");
		}
	}

	public async Task RestartAsync()
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			PT.Print("Restarting StyLua..");

			Shutdown();
			await InitAsync();

			PT.Print("Restarted StyLua successfully");
		}
		catch (Exception ex)
		{
			PT.PrintErr($"An error occurred while trying to restart stylua: {ex.Message}");
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	public void Shutdown()
	{
		_client?.Dispose();

		_restartDebounceCts?.Cancel();
		_restartDebounceCts?.Dispose();

		_fileSystemWatcher?.Created -= OnConfigChange;
		_fileSystemWatcher?.Deleted -= OnConfigChange;
		_fileSystemWatcher?.Changed -= OnConfigChange;

		_fileSystemWatcher?.Dispose();

		if (_styLuaProcess != null && !_styLuaProcess.HasExited)
		{
			_styLuaProcess.Kill();
			_styLuaProcess.Dispose();
		}
	}
}
