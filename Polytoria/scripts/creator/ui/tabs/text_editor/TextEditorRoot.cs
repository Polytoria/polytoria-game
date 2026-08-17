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
using System.Linq;

namespace Polytoria.Creator.UI.TextEditor;

public partial class TextEditorRoot : Node
{
	private const string CodeCompletionIconPath = "res://assets/textures/creator/tabs/text_editor/code_completion/";
	public const int DiagDelay = 500;

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
		_finder.TargetCodeEdit = CodeEditor;
		base._EnterTree();
	}

	public const int FormatMenuId = 10010;
	public const int FindMenuId = 10011;
	public const int ZoomInMenuId = 10012;
	public const int ZoomOutMenuId = 10013;
	public const int ZoomResetMenuId = 10014;
	public const int ToggleCommentMenuId = 10015;
	public const int ToggleBlockCommentMenuId = 10016;

	public const int FontSizeStep = 2;
	public const int MinFontSize = 8;
	public const int MaxFontSize = 72;
	private const string BlockCommentStart = "--[[";
	private const string BlockCommentEnd = "--]]";

	private static readonly Key[] NativeEditShortcutKeys = [Key.Z, Key.Y, Key.C, Key.X, Key.V, Key.A];

	public override async void _ExitTree()
	{
		if (_completion != null)
		{
			await _completion.CloseScriptAsync(Container.TargetFilePathAbsolute);
			_completion.PublishDiagnostics -= OnPublishDiagnostics;
		}
		CreatorSettingsService.Instance.Changed -= OnCreatorSettingChanged;
		CodeEditor.GetMenu().IdPressed -= OnContextMenuIdPressed;
		base._ExitTree();
	}

	public override async void _Ready()
	{
		AddChild(_autoCompleteTimer = new());
		_autoCompleteTimer.OneShot = true;
		_autoCompleteTimer.Timeout += OnCompletionRequest;

		if (Container.CodeCompletion == FileTypeEnum.Lua)
		{
			_completion = Container.TargetSession.LuaCompletion;
			_completion?.PublishDiagnostics += OnPublishDiagnostics;

			CodeEditor.CodeCompletionPrefixes = [".", ":", "\n", ",", " ", "("];
			CodeEditor.CodeCompletionEnabled = true;
			CodeEditor.CodeCompletionRequested += OnCompletionRequest;
		}

		CodeEditor.Text = File.ReadAllText(Container.TargetFilePathAbsolute);
		CodeEditor.ClearUndoHistory();
		CodeEditor.TextChanged += OnCodeEditTextChanged;
		InitSyntaxHighlighter(Container.CodeCompletion);

		PopulateStandardMenu(CodeEditor, Container.CodeCompletion == FileTypeEnum.Lua);
		CodeEditor.GetMenu().IdPressed += OnContextMenuIdPressed;

		CreatorSettingsService.Instance.Changed += OnCreatorSettingChanged;
		ApplyIndentSettings();

		CodeEditor.GuiInput += OnCodeEditGUIInput;

		CodeEditor.GuttersDrawLineNumbers = true;

		CodeEditor.AddGutter(0);
		CodeEditor.SetGutterWidth(0, 20);
		CodeEditor.SetGutterType(0, CodeEdit.GutterType.Icon);
		CodeEditor.SetGutterName(0, "diagnostics");

		CodeEditor.Root = this;
		GrabFocus();

		if (_completion != null)
		{
			await _completion.OpenScriptAsync(Container.TargetFilePathAbsolute);
		}

		UpdateStatusBar();
	}

	public static bool ShouldSwallowModifierInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true } key) return false;
		if (!key.CtrlPressed && !key.AltPressed && !key.MetaPressed) return false;
		return !NativeEditShortcutKeys.Contains(key.Keycode);
	}

	// Needs to be call deferred to be the last to grab
	public void GrabFocus()
	{
		PT.CallDeferred(CodeEditor.GrabFocus);
	}

	private async void OnContextMenuIdPressed(long id)
	{
		await HandleStandardMenuId(id, CodeEditor, _finder, Container.TargetFilePathAbsolute, OnCodeEditTextChanged, Container.CodeCompletion == FileTypeEnum.Lua);
	}

	public static async Task HandleStandardMenuId(long id, CodeEdit codeEdit, TextEditorFind finder, string formatPath, Action onTextChanged, bool canFormat)
	{
		switch (id)
		{
			case FormatMenuId:
				{
					if (canFormat)
					{
						codeEdit.Text = await LuaFormatService.FormatScriptAsync(formatPath, codeEdit.Text);
						onTextChanged();
					}
					break;
				}
			case FindMenuId:
				{
					finder.Open(codeEdit.GetSelectedText());
					break;
				}
			case ZoomInMenuId:
				{
					Zoom(codeEdit, FontSizeStep);
					break;
				}
			case ZoomOutMenuId:
				{
					Zoom(codeEdit, -FontSizeStep);
					break;
				}
			case ZoomResetMenuId:
				{
					ResetZoom(codeEdit);
					break;
				}
			case ToggleCommentMenuId:
				{
					ToggleComment(codeEdit);
					break;
				}
			case ToggleBlockCommentMenuId:
				{
					ToggleBlockComment(codeEdit);
					break;
				}
		}
	}

	public static void PopulateStandardMenu(CodeEdit codeEdit, bool canFormat)
	{
		PopupMenu menu = codeEdit.GetMenu();
		menu.AddSeparator();
		menu.AddItem("Format Script", id: FormatMenuId);
		menu.SetItemDisabled(menu.GetItemIndex(FormatMenuId), !canFormat);
		menu.AddItem("Find / Replace", id: FindMenuId);
		menu.AddSeparator();
		menu.AddItem("Zoom In", id: ZoomInMenuId);
		menu.AddItem("Zoom Out", id: ZoomOutMenuId);
		menu.AddItem("Restore Zoom", id: ZoomResetMenuId);
		menu.AddSeparator();
		menu.AddItem("Toggle Comment", id: ToggleCommentMenuId);
		menu.AddItem("Toggle Block Comment", id: ToggleBlockCommentMenuId);

		(int Id, Key Key, bool Shift)[] accelerators =
		[
			(FindMenuId, Key.F, false),
			(ZoomInMenuId, Key.Equal, false),
			(ZoomOutMenuId, Key.Minus, false),
			(ZoomResetMenuId, Key.Key0, false),
			(ToggleCommentMenuId, Key.Slash, false),
			(ToggleBlockCommentMenuId, Key.Slash, true)
		];
		foreach (var (id, key, shift) in accelerators)
		{
			Key mods = (Key)((int)KeyModifierMask.MaskCtrl | (shift ? (int)KeyModifierMask.MaskShift : 0));
			menu.SetItemAccelerator(menu.GetItemIndex(id), key | mods);
		}
	}

	public static void Zoom(CodeEdit codeEdit, int delta)
	{
		int current = codeEdit.GetThemeFontSize("font_size");
		codeEdit.AddThemeFontSizeOverride("font_size", Mathf.Clamp(current + delta, MinFontSize, MaxFontSize));
	}

	public static void ResetZoom(CodeEdit codeEdit)
	{
		codeEdit.RemoveThemeFontSizeOverride("font_size");
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

			ApplyDiagnostics(CodeEditor, _diagLabel, diagnostics);
		}
		catch (TaskCanceledException) { }
	}

	public static void ApplyDiagnostics(CodeEdit codeEdit, Label diagLabel, List<LspDiagnostic> diagnostics)
	{
		ClearDiagnostics(codeEdit, diagLabel);

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
			codeEdit.SetLineBackgroundColor(line, setTo);
			codeEdit.SetLineGutterIcon(line, 0, gutterIcon);

			if (diag.Severity == 1 && messages.Count < 5)
			{
				messages.Add($"({diag.Range.Start.Line + 1}:{diag.Range.Start.Character}): {diag.Message}");
			}
		}

		if (messages.Count > 0)
		{
			diagLabel.Text = string.Join('\n', messages);
			diagLabel.Visible = true;
		}
	}

	public static void ClearDiagnostics(CodeEdit codeEdit, Label diagLabel)
	{
		diagLabel.Text = "";
		diagLabel.Visible = false;
		Color to = new(0, 0, 0, 0);
		for (int i = 0; i < codeEdit.GetLineCount(); i++)
		{
			codeEdit.SetLineBackgroundColor(i, to);
			codeEdit.SetLineGutterIcon(i, 0, null);
		}
	}

	private async void OnCodeEditGUIInput(InputEvent @event)
	{
		if (@event.IsActionPressed("save"))
		{
			CodeEditor.AcceptEvent();
			await Save();
			Saved = true;
			SavedChanged?.Invoke(true);

			CreatorService.Interface.StatusBar?.SetStatus("Text file saved to " + Container.TargetFilePath + " at " + DateTime.Now.ToString("HH:mm:ss"));
		}
		else if (@event.IsActionPressed("textedit_find") || @event.IsActionPressed("textedit_replace"))
		{
			CodeEditor.AcceptEvent();
			_finder.Open(CodeEditor.GetSelectedText());
		}
		else if (@event.IsActionPressed("textedit_toggle_comment") && @event is InputEventKey { ShiftPressed: false })
		{
			CodeEditor.AcceptEvent();
			ToggleComment();
		}
		else if (@event.IsActionPressed("ui_cancel") && _finder.Active)
		{
			CodeEditor.AcceptEvent();
			_finder.Close();
		}
		else if (ShouldSwallowModifierInput(@event))
		{
			CodeEditor.AcceptEvent();
		}
		else
		{
			UpdateStatusBar();
		}
	}

	private void InitSyntaxHighlighter(FileTypeEnum fileType)
	{
		if (fileType == FileTypeEnum.Lua)
		{
			_highlighter = CreateLuaHighlighter();
			CodeEditor.SyntaxHighlighter = _highlighter;
			ApplyLuaStringDelimiters(CodeEditor);
		}
		else
		{
			_highlighter = new()
			{
				FunctionColor = ColorWhite,
				MemberVariableColor = ColorWhite,
				NumberColor = ColorWhite,
				SymbolColor = ColorWhite
			};
			CodeEditor.SyntaxHighlighter = _highlighter;
		}
	}

	public static CodeHighlighter CreateLuaHighlighter()
	{
		CodeHighlighter highlighter = new()
		{
			FunctionColor = ColorWarn,
			MemberVariableColor = ColorWhite,
			NumberColor = ColorSuccess,
			SymbolColor = ColorWhite
		};

		foreach (string item in LuaCompletionService.LuaKeywords)
		{
			highlighter.AddKeywordColor(item, ColorDanger);
		}

		highlighter.AddColorRegion("\"", "\"", ColorWarn);
		highlighter.AddColorRegion("'", "'", ColorWarn);
		highlighter.AddColorRegion("`", "`", ColorWarn);
		highlighter.AddColorRegion("[[", "]]", ColorWarn);
		highlighter.AddColorRegion("--[[", "]]", ColorGrey);
		highlighter.AddColorRegion("--", "", ColorGrey);

		return highlighter;
	}

	public static void ApplyLuaStringDelimiters(CodeEdit edit)
	{
		edit.AddStringDelimiter("\"", "\"", true);
		edit.AddStringDelimiter("'", "'", true);
		edit.AddStringDelimiter("[[", "]]", false);
	}

	public async Task Save()
	{
		bool formatOnSave = CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.CodeEditor.FormatOnSave);
		if (formatOnSave && Container.CodeCompletion == FileTypeEnum.Lua)
		{
			CodeEditor.Text = await LuaFormatService.FormatScriptAsync(Container.TargetFilePathAbsolute, CodeEditor.Text);
		}

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

				if (IsCompletionTrigger(CodeEditor))
				{
					OnCompletionRequest();
				}
			}
		}
	}

	public static bool IsCompletionTrigger(CodeEdit codeEdit)
	{
		int line = codeEdit.GetCaretLine();
		int col = codeEdit.GetCaretColumn();
		string lineText = codeEdit.GetLine(line);

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
		await RequestCompletions(CodeEditor, _completion, Container.TargetFilePathAbsolute, iconBasePath: CodeCompletionIconPath, skipIfMatches: GetWordBeforeCaret(CodeEditor));
	}

	public static async Task RequestCompletions(CodeEdit codeEdit, LuaCompletionService completion, string scriptPath, string? iconBasePath = null, string? skipIfMatches = null)
	{
		CodeEditCompletionContext context = new()
		{
			ScriptPath = scriptPath,
			Content = codeEdit.Text,
			CursorLine = codeEdit.GetCaretLine(),
			CursorColumn = codeEdit.GetCaretColumn()
		};

		List<CodeEditCompletionItem> items = await completion.GetCompletionsAsync(context);

		if (skipIfMatches != null)
		{
			foreach (CodeEditCompletionItem item in items)
			{
				if (skipIfMatches == item.InsertText) return;
			}
		}

		foreach (CodeEditCompletionItem item in items)
		{
			Texture2D? icon = null;
			if (iconBasePath != null)
			{
				string? iconTxt = item.Kind switch
				{
					CodeEdit.CodeCompletionKind.Member => "Property",
					CodeEdit.CodeCompletionKind.Function => "Method",
					_ => "None"
				};
				if (iconTxt != null)
				{
					icon = GD.Load<Texture2D>(iconBasePath.PathJoin(iconTxt + ".svg"));
				}
			}
			codeEdit.AddCodeCompletionOption(item.Kind, item.DisplayText, item.InsertText, icon: icon, location: -1);
		}
		codeEdit.UpdateCodeCompletionOptions(false);
	}

	private void UpdateStatusBar()
	{
		int lineIndex = CodeEditor.GetCaretLine() + 1;
		int column = CodeEditor.GetCaretColumn() + 1;
		_statusBar.Text = $"{Container.OriginTabName}: ({lineIndex}:{column})";
	}

	public static string GetWordBeforeCaret(CodeEdit codeEdit)
	{
		int lineIndex = codeEdit.GetCaretLine();
		int column = codeEdit.GetCaretColumn();
		string lineText = codeEdit.GetLine(lineIndex);

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

		return lineText[startPos..column];
	}

	public void ToggleComment() => ToggleComment(CodeEditor);

	public static IEnumerable<int> GetSelectedLines(CodeEdit codeEdit)
	{
		for (int caretIdx = 0; caretIdx < codeEdit.GetCaretCount(); caretIdx++)
		{
			for (int lineIdx = codeEdit.GetSelectionFromLine(caretIdx); lineIdx <= codeEdit.GetSelectionToLine(caretIdx); lineIdx++)
			{
				yield return lineIdx;
			}
		}
	}

	public static void ToggleComment(CodeEdit codeEdit)
	{
		List<int> lines = [.. GetSelectedLines(codeEdit)];
		if (lines.Count == 0) return;

		bool commented = lines.All(l => codeEdit.GetLine(l).StartsWith("--"));
		foreach (int lineIdx in lines)
		{
			string lineText = codeEdit.GetLine(lineIdx);
			codeEdit.SetLine(lineIdx, commented ? lineText[2..] : "--" + lineText);
		}

		codeEdit.Select(lines[0], 0, lines[^1], codeEdit.GetLine(lines[^1]).Length);
	}

	public static void ToggleBlockComment(CodeEdit codeEdit)
	{
		const int caret = 0;
		bool hasSelection = codeEdit.HasSelection(caret);

		int fromLine = hasSelection ? codeEdit.GetSelectionFromLine(caret) : codeEdit.GetCaretLine();
		int toLine = hasSelection ? codeEdit.GetSelectionToLine(caret) : codeEdit.GetCaretLine();
		bool isWrapped = fromLine > 0 && toLine < codeEdit.GetLineCount() - 1 && codeEdit.GetLine(fromLine - 1).Trim() == BlockCommentStart && codeEdit.GetLine(toLine + 1).Trim() == BlockCommentEnd;

		codeEdit.BeginComplexOperation();

		if (isWrapped)
		{
			RemoveEntireLine(codeEdit, toLine + 1);
			RemoveEntireLine(codeEdit, fromLine - 1);

			int newFrom = fromLine - 1;
			int newTo = toLine - 1;
			codeEdit.Select(newFrom, 0, newTo, codeEdit.GetLine(newTo).Length);
			codeEdit.UnindentLines();
		}
		else
		{
			codeEdit.Select(fromLine, 0, toLine, codeEdit.GetLine(toLine).Length);
			codeEdit.IndentLines();
			codeEdit.Deselect();

			codeEdit.SetCaretLine(toLine);
			codeEdit.SetCaretColumn(codeEdit.GetLine(toLine).Length);
			codeEdit.InsertTextAtCaret("\n" + BlockCommentEnd);

			codeEdit.SetCaretLine(fromLine);
			codeEdit.SetCaretColumn(0);
			codeEdit.InsertTextAtCaret(BlockCommentStart + "\n");

			int newFrom = fromLine + 1;
			int newTo = toLine + 1;
			codeEdit.Select(newFrom, 0, newTo, codeEdit.GetLine(newTo).Length);
		}

		codeEdit.EndComplexOperation();
	}

	private static void RemoveEntireLine(CodeEdit codeEdit, int lineIdx)
	{
		if (codeEdit.GetLineCount() > lineIdx + 1)
		{
			codeEdit.RemoveText(lineIdx, 0, lineIdx + 1, 0);
		}
		else if (lineIdx > 0)
		{
			int prevLen = codeEdit.GetLine(lineIdx - 1).Length;
			codeEdit.RemoveText(lineIdx - 1, prevLen, lineIdx, codeEdit.GetLine(lineIdx).Length);
		}
		else
		{
			codeEdit.SetLine(lineIdx, "");
		}
	}
}
