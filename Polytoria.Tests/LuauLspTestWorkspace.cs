// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.LSP;
using Polytoria.Creator.LSP.Schemas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Tests;

internal sealed class LuauLspTestWorkspace : IDisposable
{
	private readonly string _repositoryRoot;
	private Process? _process;
	private LspClient? _client;
	private bool _disposed;

	public string RootPath { get; }
	public string PluginDirectory => Path.Join(RootPath, ".poly", "luau");
	public LspClient Client => _client ?? throw new InvalidOperationException("The Luau LSP test client has not been started.");

	public LuauLspTestWorkspace(string namePrefix)
	{
		_repositoryRoot = FindRepositoryRoot();
		RootPath = Path.Join(Path.GetTempPath(), namePrefix + "-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(PluginDirectory);
		File.WriteAllText(Path.Join(RootPath, ".luaurc"), "{\n\t\"languageMode\": \"nocheck\"\n}\n");
		File.WriteAllText(Path.Join(PluginDirectory, "def.d.luau"), "");
		CopyPlugin("polytoria-require.luau");
		CopyPlugin("polytoria-module-types.luau");
	}

	public string WriteFile(string relativePath, string content)
	{
		string absolutePath = GetPath(relativePath);
		string? directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllText(absolutePath, content);
		return absolutePath;
	}

	public string GetPath(string relativePath)
	{
		return Path.GetFullPath(Path.Join(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	public async Task RestartAsync(
		IEnumerable<(string Path, string Source)> openDocuments,
		CancellationToken cancellationToken)
	{
		StopClient();

		_process = StartLanguageServer();
		_ = _process.StandardError.ReadToEndAsync(cancellationToken);

		_client = new LspClient(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);
		await _client.InitializeAsync(RootPath);

		foreach ((string path, string source) in openDocuments)
		{
			await _client.DidOpenAsync(path, "luau", source);
		}
	}

	public async Task<HashSet<string>> WaitForCompletionsAsync(
		string filePath,
		int line,
		int column,
		Func<HashSet<string>, bool> condition,
		CancellationToken testCancellation)
	{
		HashSet<string> labels = [];
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
		timeout.CancelAfter(TimeSpan.FromSeconds(15));

		while (!timeout.IsCancellationRequested)
		{
			try
			{
				LspCompletionItem[]? items = await Client.RequestCompletionAsync(
					filePath,
					line,
					column,
					timeout.Token);

				labels = items?
					.Select(item => item.Label ?? "")
					.Where(label => label.Length > 0)
					.ToHashSet(StringComparer.Ordinal) ?? [];

				if (condition(labels))
				{
					break;
				}

				await Task.Delay(200, timeout.Token);
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested)
			{
				break;
			}
		}

		return labels;
	}

	private Process StartLanguageServer()
	{
		string platformDirectory;
		string executableName;

		if (OperatingSystem.IsWindows())
		{
			platformDirectory = "windows";
			executableName = "luau-lsp.exe";
		}
		else if (OperatingSystem.IsLinux())
		{
			platformDirectory = "linux";
			executableName = "luau-lsp";
		}
		else if (OperatingSystem.IsMacOS())
		{
			platformDirectory = "macos";
			executableName = "luau-lsp";
		}
		else
		{
			throw new PlatformNotSupportedException("Luau LSP regression tests require Windows, Linux, or macOS.");
		}

		string executablePath = Path.Join(
			_repositoryRoot,
			"Polytoria",
			"native",
			"luau-lsp",
			platformDirectory,
			executableName);

		Assert.True(File.Exists(executablePath), $"Bundled Luau LSP was not found at {executablePath}");

		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(
				executablePath,
				UnixFileMode.UserRead |
				UnixFileMode.UserWrite |
				UnixFileMode.UserExecute |
				UnixFileMode.GroupRead |
				UnixFileMode.GroupExecute |
				UnixFileMode.OtherRead |
				UnixFileMode.OtherExecute);
		}

		ProcessStartInfo startInfo = new()
		{
			FileName = executablePath,
			Arguments = "lsp --stdio --definitions=@poly=.poly/luau/def.d.luau",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = RootPath
		};

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the bundled Luau LSP.");
	}

	private void CopyPlugin(string fileName)
	{
		string sourcePath = Path.Join(
			_repositoryRoot,
			"Polytoria",
			"modules",
			"creator",
			"codehint",
			"luau",
			fileName);

		Assert.True(File.Exists(sourcePath), $"Luau plugin was not found at {sourcePath}");
		File.Copy(sourcePath, Path.Join(PluginDirectory, fileName));
	}

	private void StopClient()
	{
		if (_process != null)
		{
			try
			{
				if (!_process.HasExited)
				{
					_process.Kill(entireProcessTree: true);
					_process.WaitForExit(5000);
				}
			}
			catch (InvalidOperationException)
			{
				// The process exited while the test was cleaning up.
			}
		}

		_client?.Dispose();
		_process?.Dispose();
		_client = null;
		_process = null;
	}

	private static string FindRepositoryRoot()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Join(directory.FullName, "Polytoria.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate the Polytoria repository root from the test output directory.");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		StopClient();

		if (Directory.Exists(RootPath))
		{
			Directory.Delete(RootPath, recursive: true);
		}
	}
}
