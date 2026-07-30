// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Polytoria.Creator.UI.TextEditor;
using Polytoria.Datamodel.Creator;
using Polytoria.Scripting;
using Polytoria.Creator.LSP;
using Polytoria.Creator.LSP.Schemas;
using Polytoria.Shared;

namespace Polytoria.Creator.UI;

public partial class ConsoleCommandLine : Control
{
	private const int MinVisibleLines = 1;
	private const int MaxVisibleLines = 4;
	private const int MaxRecents = 30;
	private const string NonstrictLuauRcContent = "{\n\t\"languageMode\": \"nonstrict\"\n}";
	private const ScriptPermissionFlags standardPermissionFlags = ScriptPermissionFlags.IORead | ScriptPermissionFlags.IOWrite | ScriptPermissionFlags.ContextAccess;
	private const ScriptPermissionFlags debugPermissionFlags = standardPermissionFlags | ScriptPermissionFlags.CreatorAccess;

	private string _temporaryFile = null!;
	private string _recentsFolder = null!;
	private string _scratchFolder = null!;
	private Godot.Timer? _completionRetryTimer;

	[Export] private Label _diagLabel = null!;
	[Export] private CodeEdit _codeEdit = null!;
	[Export] private Button _runButton = null!;
	[Export] private TextEditorFind _finder = null!;

	private int _lineHeight;
	private LuaCompletionService? _completion;

	private string? _activeSnippetPath;
	private CreatorSession? _activeSnippetSession;

	private int _historyNavIndex = -1;
	private bool _isNavigatingHistory = false;
	private CancellationTokenSource? _diagCts;

	public IEnumerable<string> GetRecentSnippetPaths()
	{
		if (!Directory.Exists(_recentsFolder))
		{
			return [];
		}
		return Directory.GetFiles(_recentsFolder, "recent*.luau").OrderByDescending(File.GetLastWriteTimeUtc);
	}

	public override void _Ready()
	{
		_recentsFolder = Path.Combine(CreatorService.DocumentsRootPath, "Commands");

		string sessionRoot = CreatorService.CurrentSession?.PolyFolderPath ?? ProjectSettings.GlobalizePath("user://creator");
		_scratchFolder = Path.Combine(sessionRoot, "console");
		_temporaryFile = Path.Combine(_scratchFolder, $"scratch_{Guid.NewGuid():N}.luau");

		_codeEdit.SyntaxHighlighter = TextEditorRoot.CreateLuaHighlighter();
		TextEditorRoot.ApplyLuaStringDelimiters(_codeEdit);

		_finder.TargetCodeEdit = _codeEdit;
		TextEditorRoot.PopulateStandardMenu(_codeEdit);
		_codeEdit.GetMenu().IdPressed += OnContextMenuIdPressed;

		_lineHeight = _codeEdit.GetLineHeight();

		_codeEdit.TextChanged += OnTextChanged;
		_codeEdit.GuiInput += OnCodeEditGuiInput;
		_runButton.Pressed += Submit;

		TryBindCompletion();
		UpdateHeight();
	}

	private void TryBindCompletion()
	{
		_completion = CreatorService.CurrentSession?.LuaCompletion;
		if (_completion == null)
		{
			_completionRetryTimer = new() { OneShot = true, WaitTime = 0.25 };
			AddChild(_completionRetryTimer);
			_completionRetryTimer.Timeout += TryBindCompletion;
			_completionRetryTimer.Start();
			return;
		}

		_completion.PublishDiagnostics += OnPublishDiagnostics;
		_codeEdit.CodeCompletionPrefixes = [".", ":", "\n", ",", " ", "("];
		_codeEdit.CodeCompletionEnabled = true;
		_codeEdit.CodeCompletionRequested += OnCompletionRequest;

		Directory.CreateDirectory(_scratchFolder);
		EnsureNonstrictOverride(_scratchFolder);
		if (!File.Exists(_temporaryFile))
		{
			File.WriteAllText(_temporaryFile, _codeEdit.Text);
		}
		_ = _completion.OpenScriptAsync(_temporaryFile);
	}

	private static void EnsureNonstrictOverride(string scratchFolder)
	{
		string luaurcPath = Path.Combine(scratchFolder, ".luaurc");
		if (!File.Exists(luaurcPath))
		{
			File.WriteAllText(luaurcPath, NonstrictLuauRcContent);
		}
	}

	public override async void _ExitTree()
	{
		_codeEdit.TextChanged -= OnTextChanged;
		_codeEdit.GuiInput -= OnCodeEditGuiInput;
		_runButton.Pressed -= Submit;
		_codeEdit.GetMenu().IdPressed -= OnContextMenuIdPressed;
		_codeEdit.CodeCompletionRequested -= OnCompletionRequest;
		_completionRetryTimer?.Stop();

		if (_completion != null)
		{
			await _completion.CloseScriptAsync(_temporaryFile);
			_completion.PublishDiagnostics -= OnPublishDiagnostics;
		}
		TextEditorRoot.ClearDiagnostics(_codeEdit, _diagLabel);

		if (File.Exists(_temporaryFile))
		{
			try { File.Delete(_temporaryFile); } catch (IOException) { }
		}

		base._ExitTree();
	}

	private void OnCodeEditGuiInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, CtrlPressed: true } key)
		{
			if (key.Keycode == Key.Up)
			{
				_codeEdit.AcceptEvent();
				NavigateHistory(older: true);
				return;
			}
			if (key.Keycode == Key.Down)
			{
				_codeEdit.AcceptEvent();
				NavigateHistory(older: false);
				return;
			}
		}

		if (@event.IsActionPressed("textedit_find") || @event.IsActionPressed("textedit_replace"))
		{
			_codeEdit.AcceptEvent();
			_finder.Open(_codeEdit.GetSelectedText());
		}
		else if (@event.IsActionPressed("ui_cancel") && _finder.Active)
		{
			_codeEdit.AcceptEvent();
			_finder.Close();
		}
		else if (TextEditorRoot.ShouldSwallowModifierInput(@event))
		{
			_codeEdit.AcceptEvent();
		}
	}

	private async void OnContextMenuIdPressed(long id)
	{
		await TextEditorRoot.HandleStandardMenuId(id, _codeEdit, _finder, _temporaryFile, OnTextChanged);
	}

	private void NavigateHistory(bool older)
	{
		List<string> recents = [.. GetRecentSnippetPaths()];

		if (older)
		{
			if (recents.Count == 0) return;
			_historyNavIndex = Math.Min(_historyNavIndex + 1, recents.Count - 1);
			LoadSnippet(recents[_historyNavIndex]);
			return;
		}

		if (_historyNavIndex <= 0 || recents.Count == 0)
		{
			_historyNavIndex = -1;

			_isNavigatingHistory = true;
			_codeEdit.Text = "";
			_isNavigatingHistory = false;

			_activeSnippetPath = null;
			_activeSnippetSession = null;
			UpdateHeight();
			return;
		}

		_historyNavIndex--;
		LoadSnippet(recents[_historyNavIndex]);
	}

	public void LoadSnippet(string path)
	{
		if (!File.Exists(path)) return;
		string content = File.ReadAllText(path);

		_isNavigatingHistory = true;
		_codeEdit.Text = content;
		_isNavigatingHistory = false;

		_activeSnippetPath = path;
		_activeSnippetSession = CreatorService.CurrentSession;

		TextEditorRoot.ClearDiagnostics(_codeEdit, _diagLabel);
		_completion?.UpdateScriptChangeAsync(_temporaryFile, content);

		UpdateHeight();
	}

	private void OnTextChanged()
	{
		if (!_isNavigatingHistory)
		{
			_historyNavIndex = -1;

			if (string.IsNullOrWhiteSpace(_codeEdit.Text))
			{
				_activeSnippetPath = null;
				_activeSnippetSession = null;
			}
		}
		_completion?.UpdateScriptChangeAsync(_temporaryFile, _codeEdit.Text);
		UpdateHeight();
	}

	private void UpdateHeight()
	{
		int visibleLines = Mathf.Clamp(_codeEdit.GetLineCount(), MinVisibleLines, MaxVisibleLines);
		_codeEdit.CustomMinimumSize = _codeEdit.CustomMinimumSize with { Y = visibleLines * _lineHeight };
	}

	private async void OnPublishDiagnostics(string path, List<LspDiagnostic> diagnostics)
	{
		if (path != _temporaryFile) return;

		_diagCts?.Cancel();
		_diagCts = new CancellationTokenSource();
		CancellationToken token = _diagCts.Token;

		try
		{
			await Task.Delay(TextEditorRoot.DiagDelay, token);
			TextEditorRoot.ApplyDiagnostics(_codeEdit, _diagLabel, diagnostics);
		}
		catch (TaskCanceledException) { }
	}

	private async void OnCompletionRequest()
	{
		if (_completion == null) return;
		await TextEditorRoot.RequestCompletions(_codeEdit, _completion, _temporaryFile);
	}

	// Recalculates recent commands every commit from oldest to newest
	private string CommitRecent(string content, string? overriddenPath)
	{
		Directory.CreateDirectory(_recentsFolder);

		List<string> allExisting = [.. GetRecentSnippetPaths()];
		List<string> keep = overriddenPath == null ? allExisting : [.. allExisting.Where(p => p != overriddenPath)];

		List<string> contents = [content, .. keep.Select(File.ReadAllText)];
		if (contents.Count > MaxRecents)
		{
			contents.RemoveRange(MaxRecents, contents.Count - MaxRecents);
		}

		foreach (string path in allExisting)
		{
			File.Delete(path);
		}

		DateTime baseTime = DateTime.UtcNow;
		for (int i = 0; i < contents.Count; i++)
		{
			string path = Path.Combine(_recentsFolder, $"recent{i + 1}.luau");
			File.WriteAllText(path, contents[i]);
			File.SetLastWriteTimeUtc(path, baseTime - TimeSpan.FromSeconds(i));
		}

		return Path.Combine(_recentsFolder, "recent1.luau");
	}

	private void Submit()
	{
		string source = _codeEdit.Text;
		if (string.IsNullOrWhiteSpace(source)) return;

		// A different session "owns" the currently-active path (if any); don't let this run
		// replace an entry that logically belongs to a project we've since left.
		string? overriddenPath = _activeSnippetSession == CreatorService.CurrentSession ? _activeSnippetPath : null;

		_activeSnippetPath = CommitRecent(source, overriddenPath);
		_activeSnippetSession = CreatorService.CurrentSession;

		CreatorService.CurrentSession?.RunScriptSource(source, Globals.IsInGDEditor ? debugPermissionFlags : standardPermissionFlags);
		_historyNavIndex = -1;
	}
}
