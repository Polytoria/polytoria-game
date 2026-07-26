// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.LSP.Schemas;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataModelScript = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.LSP;

public class LuaCompletionService(CreatorSession session)
{
	private const int CompletionDebounceMilliseconds = 75;

	private readonly CreatorSession _session = session;
	private readonly string _workspacePath = session.ProjectFolderPath;
	private readonly SemaphoreSlim _lspGate = new(1, 1);
	private readonly object _stateLock = new();
	private Process _luaLSProcess = null!;
	private LspClient _client = null!;
	private FileSystemWatcher? _workspaceWatcher;
	private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _lastSyncedContent = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _completionGenerations = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<World> _trackedWorlds = [];
	private readonly List<World> _worldOrder = [];
	private readonly Dictionary<World, int> _worldInstanceCounts = [];
	private readonly HashSet<Instance> _trackedInstances = [];
	private readonly HashSet<DataModelScript> _trackedScripts = [];
	private readonly Dictionary<DataModelScript, ScriptMapState> _scriptMapStates = [];
	private readonly HashSet<string> _linkedModuleFiles = new(StringComparer.OrdinalIgnoreCase);
	private bool _moduleMapDirty = true;
	private bool _languageServerRefreshPending;

	private readonly record struct ScriptMapState(string WorldPath, string? LinkedPath);

	public event Action<string, List<LspDiagnostic>>? PublishDiagnostics;

	public static readonly string[] LuaKeywords =
	[
		"and", "break", "do", "else", "elseif", "end",
		"false", "for", "function", "if",
		"in", "local", "nil", "not", "or", "repeat",
		"return", "then", "true", "until", "while",
		"continue", "const"
	];

	public async Task InitAsync()
	{
		await _lspGate.WaitAsync();
		try
		{
			SynchronizeTrackedWorlds();
			FlushModuleMapIfDirty();

			// The initial server starts after the current map is already on disk, so no
			// follow-up refresh is needed for that first generated snapshot.
			ClearLanguageServerRefreshPending();
			await StartLanguageServerCoreAsync();
			StartWorkspaceWatcher();
		}
		finally
		{
			_lspGate.Release();
		}
	}

	private async Task StartLanguageServerCoreAsync()
	{
		ProcessStartInfo processStartInfo = new()
		{
			FileName = NativeBinHelper.ResolveLuauLspBinPath(),
			Arguments = "lsp --stdio --definitions=@poly=.poly/luau/def.d.luau",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = _workspacePath
		};

		_luaLSProcess = Process.Start(processStartInfo) ?? throw new Exception("Failed to start language server process");

		_luaLSProcess.ErrorDataReceived += (_, e) =>
		{
			if (!string.IsNullOrEmpty(e.Data))
			{
				PT.PrintErr($"Server Error: {e.Data}");
			}
		};

		_luaLSProcess.BeginErrorReadLine();
		PT.Print("LuaLS Started");

		_client = new LspClient(_luaLSProcess.StandardOutput.BaseStream, _luaLSProcess.StandardInput.BaseStream);
		await _client.InitializeAsync(_workspacePath);
		_client.PublishDiagnostics += OnPublishDiagnostics;

		PT.Print("Language server initialized at ", _workspacePath);
	}

	private void StopLanguageServerCore()
	{
		if (_client != null)
		{
			_client.PublishDiagnostics -= OnPublishDiagnostics;
		}

		if (_luaLSProcess != null)
		{
			try
			{
				if (!_luaLSProcess.HasExited)
				{
					_luaLSProcess.Kill();
				}
			}
			catch (InvalidOperationException)
			{
				// The process exited between HasExited and Kill.
			}
			finally
			{
				_luaLSProcess.Dispose();
				_luaLSProcess = null!;
			}
		}

		if (_client != null)
		{
			_client.Dispose();
			_client = null!;
		}
	}

	private async Task RestartLanguageServerCoreAsync()
	{
		Dictionary<string, string> openDocuments = new(_lastSyncedContent, StringComparer.OrdinalIgnoreCase);

		StopLanguageServerCore();
		await StartLanguageServerCoreAsync();

		_versions.Clear();
		foreach ((string scriptPath, string content) in openDocuments)
		{
			_versions[scriptPath] = 1;
			await _client.DidOpenAsync(scriptPath, "luau", content);
		}

		PT.Print("Luau LSP refreshed module types and reopened ", openDocuments.Count, " script(s)");
	}

	private void StartWorkspaceWatcher()
	{
		_workspaceWatcher = new FileSystemWatcher(_workspacePath)
		{
			IncludeSubdirectories = true,
			Filter = "*.*",
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
		};

		_workspaceWatcher.Changed += OnWorkspaceFileChanged;
		_workspaceWatcher.Created += OnWorkspaceFileChanged;
		_workspaceWatcher.Deleted += OnWorkspaceFileChanged;
		_workspaceWatcher.Renamed += OnWorkspaceFileRenamed;
		_workspaceWatcher.EnableRaisingEvents = true;
	}

	private void StopWorkspaceWatcher()
	{
		if (_workspaceWatcher == null)
		{
			return;
		}

		_workspaceWatcher.EnableRaisingEvents = false;
		_workspaceWatcher.Changed -= OnWorkspaceFileChanged;
		_workspaceWatcher.Created -= OnWorkspaceFileChanged;
		_workspaceWatcher.Deleted -= OnWorkspaceFileChanged;
		_workspaceWatcher.Renamed -= OnWorkspaceFileRenamed;
		_workspaceWatcher.Dispose();
		_workspaceWatcher = null;
	}

	private void OnWorkspaceFileChanged(object sender, FileSystemEventArgs args)
	{
		if (!IsLuauSourcePath(args.FullPath))
		{
			return;
		}

		string absolutePath = Path.GetFullPath(args.FullPath);
		lock (_stateLock)
		{
			if (_linkedModuleFiles.Contains(absolutePath))
			{
				_languageServerRefreshPending = true;
			}
		}
	}

	private void OnWorkspaceFileRenamed(object sender, RenamedEventArgs args)
	{
		if (!IsLuauSourcePath(args.FullPath) && !IsLuauSourcePath(args.OldFullPath))
		{
			return;
		}

		string absolutePath = Path.GetFullPath(args.FullPath);
		string oldAbsolutePath = Path.GetFullPath(args.OldFullPath);
		lock (_stateLock)
		{
			if (_linkedModuleFiles.Contains(absolutePath) || _linkedModuleFiles.Contains(oldAbsolutePath))
			{
				_moduleMapDirty = true;
				_languageServerRefreshPending = true;
			}
		}
	}

	private static bool IsLuauSourcePath(string path)
	{
		string extension = Path.GetExtension(path);
		return extension.Equals(".luau", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".lua", StringComparison.OrdinalIgnoreCase);
	}

	private void OnPublishDiagnostics(LspPublishDiagnosticsParams @params)
	{
		string normalizedUri = new Uri(@params.Uri).AbsoluteUri;
		if (_client.LspPathToFull.TryGetValue(normalizedUri, out string? fullPath))
		{
			Callable.From(() => PublishDiagnostics?.Invoke(fullPath, @params.Diagnostics)).CallDeferred();
		}
	}

	public void Shutdown()
	{
		StopWorkspaceWatcher();
		UntrackAllWorlds();
		StopLanguageServerCore();
	}

	public async Task OpenScriptAsync(string scriptPath)
	{
		await _lspGate.WaitAsync();
		try
		{
			SynchronizeTrackedWorlds();
			FlushModuleMapIfDirty();
			if (TakeLanguageServerRefreshPending())
			{
				await RestartLanguageServerCoreAsync();
			}

			string content = File.ReadAllText(scriptPath);
			_versions[scriptPath] = 1;
			_lastSyncedContent[scriptPath] = content;
			await _client.DidOpenAsync(scriptPath, "luau", content);
		}
		finally
		{
			_lspGate.Release();
		}
	}

	public async Task CloseScriptAsync(string scriptPath)
	{
		await _lspGate.WaitAsync();
		try
		{
			_versions.Remove(scriptPath);
			_lastSyncedContent.Remove(scriptPath);
			lock (_stateLock)
			{
				_completionGenerations.Remove(scriptPath);
			}
			await _client.DidCloseAsync(scriptPath);
		}
		finally
		{
			_lspGate.Release();
		}
	}

	public async Task UpdateScriptChangeAsync(string scriptPath, string scriptContent)
	{
		await _lspGate.WaitAsync();
		try
		{
			await UpdateScriptChangeCoreAsync(scriptPath, scriptContent);
		}
		finally
		{
			_lspGate.Release();
		}
	}

	private async Task UpdateScriptChangeCoreAsync(string scriptPath, string scriptContent)
	{
		if (_lastSyncedContent.TryGetValue(scriptPath, out string? previousContent) && previousContent == scriptContent)
		{
			return;
		}

		int version = _versions.TryGetValue(scriptPath, out int currentVersion) ? currentVersion + 1 : 2;
		_versions[scriptPath] = version;
		_lastSyncedContent[scriptPath] = scriptContent;
		await _client.DidChangeAsync(scriptPath, scriptContent, version);
	}

	private void SynchronizeTrackedWorlds()
	{
		bool worldOrderChanged = _worldOrder.Count != _session.OpenedWorlds.Count;
		if (!worldOrderChanged)
		{
			for (int index = 0; index < _worldOrder.Count; index++)
			{
				if (!ReferenceEquals(_worldOrder[index], _session.OpenedWorlds[index]))
				{
					worldOrderChanged = true;
					break;
				}
			}
		}

		List<World> removedWorlds = [];
		foreach (World trackedWorld in _trackedWorlds)
		{
			if (!_session.OpenedWorlds.Contains(trackedWorld))
			{
				removedWorlds.Add(trackedWorld);
			}
		}

		foreach (World removedWorld in removedWorlds)
		{
			bool containedScripts = SubtreeContainsScript(removedWorld);
			UntrackSubtree(removedWorld);
			_trackedWorlds.Remove(removedWorld);
			_worldInstanceCounts.Remove(removedWorld);
			if (containedScripts)
			{
				MarkModuleMapDirty();
			}
		}

		foreach (World world in _session.OpenedWorlds)
		{
			if (_trackedWorlds.Add(world))
			{
				_worldInstanceCounts[world] = world.InstanceCount;
				if (TrackSubtree(world))
				{
					MarkModuleMapDirty();
				}
			}
			else if (!_worldInstanceCounts.TryGetValue(world, out int previousCount) || previousCount != world.InstanceCount)
			{
				// Some import/deserialization paths add a complete subtree without emitting
				// the ordinary ChildAdded signal observed by the editor. InstanceCount is
				// authoritative, so reconcile the hierarchy whenever it changes.
				_worldInstanceCounts[world] = world.InstanceCount;
				if (TrackSubtree(world))
				{
					MarkModuleMapDirty();
				}
			}
		}

		// LinkedScript can be assigned after the Instance has entered the tree while
		// toolbox assets finish materializing their project files. Compare the small
		// script index directly so a missed property notification cannot leave the map
		// stale.
		if (RefreshScriptMapStates())
		{
			MarkModuleMapDirty();
		}

		if (worldOrderChanged)
		{
			MarkModuleMapDirty();
			_worldOrder.Clear();
			_worldOrder.AddRange(_session.OpenedWorlds);
		}
	}

	private bool TrackSubtree(Instance instance)
	{
		bool addedScript = false;
		if (_trackedInstances.Add(instance))
		{
			instance.ChildAdded.Connect(OnTrackedChildAdded);
			instance.ChildRemoved.Connect(OnTrackedChildRemoved);
			instance.Renamed.Connect(OnTrackedInstanceRenamed);

			if (instance is DataModelScript script && _trackedScripts.Add(script))
			{
				script.PropertyChanged.Connect(OnTrackedScriptPropertyChanged);
				_scriptMapStates[script] = GetScriptMapState(script);
				addedScript = true;
			}
		}

		// Always recurse. A parent may already be tracked while an imported child was
		// inserted through a path that did not emit ChildAdded.
		foreach (Instance child in instance.GetChildren())
		{
			addedScript |= TrackSubtree(child);
		}

		return addedScript;
	}

	private void UntrackSubtree(Instance instance)
	{
		if (!_trackedInstances.Remove(instance))
		{
			return;
		}

		foreach (Instance child in instance.GetChildren())
		{
			UntrackSubtree(child);
		}

		instance.ChildAdded.Disconnect(OnTrackedChildAdded);
		instance.ChildRemoved.Disconnect(OnTrackedChildRemoved);
		instance.Renamed.Disconnect(OnTrackedInstanceRenamed);

		if (instance is DataModelScript script)
		{
			script.PropertyChanged.Disconnect(OnTrackedScriptPropertyChanged);
			_trackedScripts.Remove(script);
			_scriptMapStates.Remove(script);
		}
	}

	private void UntrackAllWorlds()
	{
		List<World> worlds = [.. _trackedWorlds];
		foreach (World world in worlds)
		{
			UntrackSubtree(world);
		}
		_trackedWorlds.Clear();
		_worldOrder.Clear();
		_worldInstanceCounts.Clear();
		_scriptMapStates.Clear();
	}

	private void OnTrackedChildAdded(Instance child)
	{
		if (TrackSubtree(child))
		{
			MarkModuleMapDirty();
		}
	}

	private void OnTrackedChildRemoved(Instance child)
	{
		bool containedScripts = SubtreeContainsScript(child);
		UntrackSubtree(child);
		if (containedScripts)
		{
			MarkModuleMapDirty();
		}
	}

	private void OnTrackedInstanceRenamed()
	{
		MarkModuleMapDirty();
	}

	private void OnTrackedScriptPropertyChanged(string propertyName)
	{
		if (propertyName == nameof(DataModelScript.LinkedScript))
		{
			MarkModuleMapDirty();
		}
	}

	private bool RefreshScriptMapStates()
	{
		bool changed = false;
		foreach (DataModelScript script in _trackedScripts)
		{
			ScriptMapState current = GetScriptMapState(script);
			if (!_scriptMapStates.TryGetValue(script, out ScriptMapState previous) || previous != current)
			{
				_scriptMapStates[script] = current;
				changed = true;
			}
		}
		return changed;
	}

	private static ScriptMapState GetScriptMapState(DataModelScript script)
	{
		return new(script.LuaPath, script.LinkedScript?.LinkedPath);
	}

	private static bool SubtreeContainsScript(Instance instance)
	{
		if (instance is DataModelScript)
		{
			return true;
		}

		foreach (Instance child in instance.GetChildren())
		{
			if (SubtreeContainsScript(child))
			{
				return true;
			}
		}

		return false;
	}

	private void MarkModuleMapDirty()
	{
		lock (_stateLock)
		{
			_moduleMapDirty = true;
		}
	}

	private bool TakeModuleMapDirty()
	{
		lock (_stateLock)
		{
			if (!_moduleMapDirty)
			{
				return false;
			}

			_moduleMapDirty = false;
			return true;
		}
	}

	private void MarkLanguageServerRefreshPending()
	{
		lock (_stateLock)
		{
			_languageServerRefreshPending = true;
		}
	}

	private bool TakeLanguageServerRefreshPending()
	{
		lock (_stateLock)
		{
			if (!_languageServerRefreshPending)
			{
				return false;
			}

			_languageServerRefreshPending = false;
			return true;
		}
	}

	private void ClearLanguageServerRefreshPending()
	{
		lock (_stateLock)
		{
			_languageServerRefreshPending = false;
		}
	}

	private void FlushModuleMapIfDirty()
	{
		if (!TakeModuleMapDirty())
		{
			return;
		}

		bool mapChanged = LuauModuleMapService.Generate(_session, _trackedScripts);
		RebuildLinkedModuleFileIndex();

		if (mapChanged)
		{
			MarkLanguageServerRefreshPending();
		}
	}

	private void RebuildLinkedModuleFileIndex()
	{
		HashSet<string> currentFiles = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataModelScript script in _trackedScripts)
		{
			if (script is not ModuleScript)
			{
				continue;
			}

			string? linkedPath = script.LinkedScript?.LinkedPath;
			if (string.IsNullOrWhiteSpace(linkedPath))
			{
				continue;
			}

			string absolutePath = Path.IsPathRooted(linkedPath)
				? Path.GetFullPath(linkedPath)
				: Path.GetFullPath(Path.Join(_session.ProjectFolderPath, linkedPath));
			currentFiles.Add(absolutePath);
		}

		lock (_stateLock)
		{
			_linkedModuleFiles.Clear();
			foreach (string path in currentFiles)
			{
				_linkedModuleFiles.Add(path);
			}
		}
	}

	private long BeginCompletionRequest(string scriptPath)
	{
		lock (_stateLock)
		{
			long generation = _completionGenerations.TryGetValue(scriptPath, out long current) ? current + 1 : 1;
			_completionGenerations[scriptPath] = generation;
			return generation;
		}
	}

	private bool IsLatestCompletionRequest(string scriptPath, long generation)
	{
		lock (_stateLock)
		{
			return _completionGenerations.TryGetValue(scriptPath, out long current) && current == generation;
		}
	}

	public async Task<List<CodeEditCompletionItem>> GetCompletionsAsync(CodeEditCompletionContext context, CancellationToken? cancelToken = null)
	{
		CancellationToken cancellationToken = cancelToken ?? CancellationToken.None;
		long requestGeneration = BeginCompletionRequest(context.ScriptPath);

		try
		{
			await Task.Delay(CompletionDebounceMilliseconds, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return [];
		}

		if (!IsLatestCompletionRequest(context.ScriptPath, requestGeneration))
		{
			return [];
		}

		await _lspGate.WaitAsync(cancellationToken);
		try
		{
			if (!IsLatestCompletionRequest(context.ScriptPath, requestGeneration))
			{
				return [];
			}

			if (_lastSyncedContent.TryGetValue(context.ScriptPath, out string? latestContent))
			{
				if (!string.Equals(latestContent, context.Content, StringComparison.Ordinal))
				{
					return [];
				}
			}
			else
			{
				await UpdateScriptChangeCoreAsync(context.ScriptPath, context.Content);
			}

			SynchronizeTrackedWorlds();
			FlushModuleMapIfDirty();

			if (TakeLanguageServerRefreshPending())
			{
				await RestartLanguageServerCoreAsync();
			}

			if (!IsLatestCompletionRequest(context.ScriptPath, requestGeneration))
			{
				return [];
			}

			LspCompletionItem[]? completionResult = await _client.RequestCompletionAsync(
				context.ScriptPath,
				context.CursorLine,
				context.CursorColumn,
				cancellationToken);

			if (!IsLatestCompletionRequest(context.ScriptPath, requestGeneration))
			{
				return [];
			}

			List<CodeEditCompletionItem> items = [];
			if (completionResult != null)
			{
				foreach (LspCompletionItem item in completionResult)
				{
					CodeEdit.CodeCompletionKind kind = item.Kind switch
					{
						9 => CodeEdit.CodeCompletionKind.Function,
						3 => CodeEdit.CodeCompletionKind.Function,
						21 => CodeEdit.CodeCompletionKind.Constant,
						7 => CodeEdit.CodeCompletionKind.Class,
						13 => CodeEdit.CodeCompletionKind.Enum,
						6 => CodeEdit.CodeCompletionKind.Variable,
						20 => CodeEdit.CodeCompletionKind.Member,
						10 => CodeEdit.CodeCompletionKind.Member,
						5 => CodeEdit.CodeCompletionKind.Member,
						14 => CodeEdit.CodeCompletionKind.PlainText,
						_ => CodeEdit.CodeCompletionKind.PlainText,
					};

					items.Add(new()
					{
						DisplayText = item.Label ?? "",
						Kind = kind,
						Detail = item.Detail ?? "",
						InsertText = string.IsNullOrWhiteSpace(item.InsertText) ? item.Label ?? "" : item.InsertText
					});
				}
			}

			return items;
		}
		finally
		{
			_lspGate.Release();
		}
	}
}

public struct CodeEditCompletionItem
{
	public string DisplayText { get; set; }
	public CodeEdit.CodeCompletionKind Kind { get; set; }
	public string InsertText { get; set; }
	public string Detail { get; set; }
}

public struct CodeEditCompletionContext
{
	public string ScriptPath { get; set; }
	public string Content { get; set; }
	public int CursorLine { get; set; }
	public int CursorColumn { get; set; }
}
