// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.LSP;
using Polytoria.Creator.LSP.Schemas;
using Polytoria.Creator.Settings;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Creator.UI.TextEditor;

public partial class TextEditorRoot : Node
{
	private const string CodeCompletionIconPath = "res://assets/textures/creator/tabs/text_editor/code_completion/";
	private const int DiagDelay = 500;

	[Export] public TextEditorField CodeEditor = null!;
	public TextEditorContainer Container = null!;
	public bool Saved = false;

	public event Action<bool>? SavedChanged;

	[Export] private TextEditorFind _finder = null!;
	[Export] private Label _diagLabel = null!;
	[Export] private Label _statusBar = null!;

	public static Color ColorDanger { get; private set; } = Color.FromString("D77C79", Colors.White);
	public static Color ColorOrange { get; private set; } = Color.FromString("E6A472", Colors.White);
	public static Color ColorWarn { get; private set; } = Color.FromString("F4CF86", Colors.White);
	public static Color ColorSuccess { get; private set; } = Color.FromString("C2C77B", Colors.White);
	public static Color ColorPurple { get; private set; } = Color.FromString("C0A7C7", Colors.White);
	public static Color ColorGrey { get; private set; } = Color.FromString("A7A8A7", Colors.White);
	public static Color ColorWhite { get; private set; } = Colors.White;

	private string _oldText = "";
	private CodeHighlighter _highlighter = null!;
	private LuaCompletionService? _completion = null!;

	private Godot.Timer _autoCompleteTimer = null!;
	private CancellationTokenSource? _diagCts;

	public override void _EnterTree()
	{
		_finder.Root = this;
		base._EnterTree();
	}

	public override async void _ExitTree()
	{
		if (_completion != null)
		{
			await _completion.CloseScriptAsync(Container.TargetFilePathAbsolute);
			_completion.PublishDiagnostics -= OnPublishDiagnostics;
		}
		CreatorSettingsService.Instance.Changed -= OnCreatorSettingChanged;
		base._ExitTree();
	}

	public override async void _Ready()
	{
		AddChild(_autoCompleteTimer = new());
		_autoCompleteTimer.OneShot = true;
		_autoCompleteTimer.Timeout += OnCompletionRequest;

		_completion = Container.TargetSession.LuaCompletion;
		_completion?.PublishDiagnostics += OnPublishDiagnostics;

		CodeEditor.Text = File.ReadAllText(Container.TargetFilePathAbsolute);
		CodeEditor.ClearUndoHistory();
		CodeEditor.TextChanged += OnCodeEditTextChanged;
		InitSyntaxHighlighter();

		CreatorSettingsService.Instance.Changed += OnCreatorSettingChanged;
		ApplyIndentSettings();

		CodeEditor.CodeCompletionPrefixes = [".", ":", "\n", ",", " ", "("];
		CodeEditor.CodeCompletionEnabled = true;
		CodeEditor.CodeCompletionRequested += OnCompletionRequest;
		CodeEditor.GuiInput += OnCodeEditGUIInput;

		CodeEditor.GuttersDrawLineNumbers = true;

		CodeEditor.AddGutter(0);
		CodeEditor.SetGutterWidth(0, 20);
		CodeEditor.SetGutterType(0, CodeEdit.GutterType.Icon);
		CodeEditor.SetGutterName(0, "diagnostics");

		CodeEditor.Root = this;

		// TODO: Can be made into TextEditorRoot.GrabFocus() ?
		// Needs to be call deferred to be the last to grab
		PT.CallDeferred(CodeEditor.GrabFocus);

		if (_completion != null)
		{
			await _completion.OpenScriptAsync(Container.TargetFilePathAbsolute);
		}

		UpdateStatusBar();
	}

	private void OnCreatorSettingChanged(SettingChangedEvent e)
	{
		if (e.Key == CreatorSettingKeys.CodeEditor.IndentationMode || e.Key == CreatorSettingKeys.CodeEditor.IndentationSize)
		{
			ApplyIndentSettings();
		}
	}

	private void ApplyIndentSettings()
	{
		IndentationModeEnum indentationMode = CreatorSettingsService.Instance.Get<IndentationModeEnum>(CreatorSettingKeys.CodeEditor.IndentationMode);
		int indentationSize = CreatorSettingsService.Instance.Get<int>(CreatorSettingKeys.CodeEditor.IndentationSize);
		CodeEditor.IndentUseSpaces = indentationMode == IndentationModeEnum.Spaces;
		CodeEditor.IndentSize = indentationSize;
	}

	private async void OnPublishDiagnostics(string path, List<LspDiagnostic> diagnostics)
	{
		// If not the right path, return
		if (path != Container.TargetFilePathAbsolute) return;

		// Cancel the previous pending update
		_diagCts?.Cancel();
		_diagCts = new CancellationTokenSource();
		CancellationToken token = _diagCts.Token;

		try
		{
			await Task.Delay(DiagDelay, token);

			ApplyDiagnostics(diagnostics);
		}
		catch (TaskCanceledException) { }
	}

	private void ApplyDiagnostics(List<LspDiagnostic> diagnostics)
	{
		ClearDiagnostics();

		List<string> messages = [];

		foreach (LspDiagnostic diag in diagnostics)
		{
			int line = diag.Range.Start.Line;
			Color setTo = diag.Severity switch
			{
				1 => Color.FromHtml("#DD555520"), // Error
				_ => new(0, 0, 0, 0)
			};
			Texture2D? gutterIcon = diag.Severity switch
			{
				1 => GD.Load<Texture2D>("res://assets/textures/creator/tabs/text_editor/error.svg"), // Error
				_ => null
			};
			CodeEditor.SetLineBackgroundColor(diag.Range.Start.Line, setTo);
			CodeEditor.SetLineGutterIcon(line, 0, gutterIcon);

			if (diag.Severity == 1 && messages.Count < 5)
			{
				messages.Add($"({diag.Range.Start.Line + 1}:{diag.Range.Start.Character}): {diag.Message}");
			}
		}

		if (messages.Count > 0)
		{
			_diagLabel.Text = string.Join('\n', messages);
			_diagLabel.Visible = true;
		}
	}

	private void ClearDiagnostics()
	{
		_diagLabel.Text = "";
		_diagLabel.Visible = false;
		Color to = new(0, 0, 0, 0);
		for (int i = 0; i < CodeEditor.GetLineCount(); i++)
		{
			CodeEditor.SetLineBackgroundColor(i, to);
			CodeEditor.SetLineGutterIcon(i, 0, null);
		}
	}

	private async void OnCodeEditGUIInput(InputEvent @event)
	{
		if (@event.IsActionPressed("save"))
		{
			CodeEditor.AcceptEvent();
			Save();
			Saved = true;
			SavedChanged?.Invoke(true);
			CreatorService.Interface.StatusBar?.SetStatus("Text file saved to " + Container.TargetFilePath + " at " + DateTime.Now.ToString("HH:mm:ss"));
		}
		else if (@event.IsActionPressed("textedit_find") || @event.IsActionPressed("textedit_replace"))
		{
			CodeEditor.AcceptEvent();
			_finder.Open(CodeEditor.GetSelectedText());
		}
		else if (@event.IsActionPressed("ui_cancel"))
		{
			CodeEditor.AcceptEvent();
			_finder.Close();
		}
		else
		{
			UpdateStatusBar();
		}
	}

	private void InitSyntaxHighlighter()
	{
		_highlighter = new();
		CodeEditor.SyntaxHighlighter = _highlighter;

		foreach (string item in LuaCompletionService.LuaKeywords)
		{
			_highlighter.AddKeywordColor(item, ColorDanger);
		}

		_highlighter.AddColorRegion("\"", "\"", ColorWarn);
		_highlighter.AddColorRegion("'", "'", ColorWarn);
		_highlighter.AddColorRegion("`", "`", ColorWarn);
		_highlighter.AddColorRegion("[[", "]]", ColorWarn);
		_highlighter.AddColorRegion("--[[", "]]", ColorGrey);
		_highlighter.AddColorRegion("--", "", ColorGrey);
		_highlighter.FunctionColor = ColorWarn;
		_highlighter.MemberVariableColor = ColorWhite;
		_highlighter.NumberColor = ColorSuccess;
		_highlighter.SymbolColor = ColorWhite;

		CodeEditor.AddStringDelimiter("\"", "\"", true);
		CodeEditor.AddStringDelimiter("'", "'", true);
		CodeEditor.AddStringDelimiter("[[", "]]", false);
	}

	public void Save()
	{
		File.WriteAllText(Container.TargetFilePathAbsolute, CodeEditor.Text);
	}

	private async void OnCodeEditTextChanged()
	{
		string curText = CodeEditor.Text;
		Saved = false;
		SavedChanged?.Invoke(false);
		if (_completion != null)
		{
			await _completion.UpdateScriptChangeAsync(Container.TargetFilePathAbsolute, curText);
			if (_oldText != curText)
			{
				_oldText = curText;

				if (IsCompletionTrigger())
				{
					OnCompletionRequest();
				}
			}
		}
	}

	private bool IsCompletionTrigger()
	{
		int line = CodeEditor.GetCaretLine();
		int col = CodeEditor.GetCaretColumn();
		string lineText = CodeEditor.GetLine(line);

		if (string.IsNullOrWhiteSpace(lineText)) return false;

		if (col > 0)
		{
			char prevChar = lineText[col - 1];

			// Don't trigger on space, equals, or commas
			if (prevChar == ' ' || prevChar == '=' || prevChar == ',')
				return false;

			// Don't trigger on newlines/tabs
			if (prevChar == '\n' || prevChar == '\t')
				return false;
		}

		return true;
	}

	public async void OnCompletionRequest()
	{
		if (_completion == null) return;
		CodeEditCompletionContext context = new()
		{
			ScriptPath = Container.TargetFilePathAbsolute,
			Content = CodeEditor.Text,
			CursorLine = CodeEditor.GetCaretLine(),
			CursorColumn = CodeEditor.GetCaretColumn(),
		};

		List<CodeEditCompletionItem> items = await _completion.GetCompletionsAsync(context);

		string wcaret = GetWordBeforeCaret();

		foreach (CodeEditCompletionItem item in items)
		{
			if (wcaret == item.InsertText)
			{
				return;
			}
		}

		foreach (CodeEditCompletionItem item in items)
		{
			string? iconTxt = item.Kind switch
			{
				CodeEdit.CodeCompletionKind.Member => "Property",
				CodeEdit.CodeCompletionKind.Function => "Method",
				_ => "None"
			};
			Texture2D? icon = null;
			if (iconTxt != null)
			{
				icon = GD.Load<Texture2D>(CodeCompletionIconPath.PathJoin(iconTxt + ".svg"));
			}
			CodeEditor.AddCodeCompletionOption(item.Kind, item.DisplayText, item.InsertText, icon: icon, location: -1);
		}
		CodeEditor.UpdateCodeCompletionOptions(false);
	}

	private void UpdateStatusBar()
	{
		int lineIndex = CodeEditor.GetCaretLine() + 1;
		int column = CodeEditor.GetCaretColumn() + 1;
		_statusBar.Text = $"{Container.OriginTabName}: ({lineIndex}:{column})";
	}

	public string GetWordBeforeCaret()
	{
		int lineIndex = CodeEditor.GetCaretLine();
		int column = CodeEditor.GetCaretColumn();
		string lineText = CodeEditor.GetLine(lineIndex);

		if (column == 0) return string.Empty;

		int startPos = column;

		while (startPos > 0)
		{
			char c = lineText[startPos - 1];

			if (char.IsLetterOrDigit(c) || c == '_')
			{
				startPos--;
			}
			else
			{
				break;
			}
		}

		return lineText.Substring(startPos, column - startPos);
	}
}
