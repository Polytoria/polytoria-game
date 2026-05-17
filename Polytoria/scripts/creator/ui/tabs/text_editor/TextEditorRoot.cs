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
using MDToBBCode;
using System.Runtime.CompilerServices;

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

	[Export] public TextEditorTooltip Tooltip = null!;
	[Export(PropertyHint.Range, "0,1,or_greater,suffix:ms")]
	public int TooltipHideDelayMS = 150;

	private int? _prevHoverLine = null;
	private int? _prevHoverStartCol = null;
	private int? _prevHoverEndCol = null;
	private CancellationTokenSource? _hideTooltipCts;

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
		CodeEditor.SymbolHovered += OnSymbolHovered;
		CodeEditor.MouseExited += OnCodeEditMouseExited;

		Tooltip.MouseEntered += OnTooltipMouseEntered;
		Tooltip.MouseExited += OnTooltipMouseExited;

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
		if (@event is InputEventMouseMotion mouseMotion)
		{
			OnMouseMotion(mouseMotion);
		}

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

	private void OnMouseMotion(InputEventMouseMotion mouseMotion)
	{
		if (_prevHoverLine is null)
		{
			return;
		}

		// Convert the float mouse position to Vector2I
		Vector2I mousePos = new((int)mouseMotion.Position.X, (int)mouseMotion.Position.Y);

		// The 'false' argument here is CRITICAL. 
		// It tells Godot: "If the mouse is past the end of the text, return (-1, -1)"
		Vector2I hovered = CodeEditor.GetLineColumnAtPos(mousePos, false);

		int currentLine = hovered.Y;
		int currentCol = hovered.X;

		// Are we inside the boundaries of the currently displayed tooltip's symbol?
		bool isInsideCachedSymbol = _prevHoverLine != -1 &&
									currentLine == _prevHoverLine &&
									currentCol >= _prevHoverStartCol &&
									currentCol <= _prevHoverEndCol;

		if (isInsideCachedSymbol)
		{
			// We moved, but we are still inside the "safe zone" of the symbol.
			// Cancel any potential hide operation that might have been triggered by leaving the tooltip.
			_hideTooltipCts?.Cancel();
		}
		else if (Tooltip.Visible)
		{
			// We are outside the symbol's boundaries. Start the hide timer.
			StartDelayedHideTooltip();
		}
	}

	private void OnTooltipMouseEntered()
	{
		_hideTooltipCts?.Cancel();
	}

	private void OnTooltipMouseExited()
	{
		if (!Tooltip.GetGlobalRect().HasPoint(Tooltip.GetGlobalMousePosition()))
		{
			StartDelayedHideTooltip();
		}
	}

	private void OnCodeEditMouseExited()
	{
		StartDelayedHideTooltip();
	}

	private async void StartDelayedHideTooltip()
	{
		// Cancel and dispose of any previous cancellation tokens.
		_hideTooltipCts?.Cancel();
		_hideTooltipCts?.Dispose();
		_hideTooltipCts = new();

		try
		{
			await Task.Delay(TooltipHideDelayMS, _hideTooltipCts.Token);

			Tooltip.Visible = false;
			_prevHoverLine = null;
		}
		catch (TaskCanceledException)
		{
			// Task is cancelled, do nothing.
			return;
		}
	}

	/// <summary>
	/// Handles the event when a symbol is hovered in the code editor and displays relevant tooltip information for the
	/// symbol at the specified line and column.
	/// </summary>
	/// <remarks>
	/// If the hovered position is not over a valid symbol character or the code completion service is not
	/// ready, the tooltip is hidden or not updated. The tooltip is only shown when hovering over a valid symbol, and its
	/// content is retrieved asynchronously using Luau-LSP.
	/// 
	/// P.S. To this day, I am still baffled by why line and column are longs here instead of ints.
	/// </remarks>
	/// <param name="symbol">The symbol being hovered over in the code editor.</param>
	/// <param name="line">The zero-based line number where the symbol is located.</param>
	/// <param name="column">The zero-based column number within the line where the symbol is located.</param>
	private async void OnSymbolHovered(string symbol, long line, long column)
	{
		_hideTooltipCts?.Cancel();

		int hoveredSymbolLine = (int)line;
		int hoveredSymbolCol = (int)column;

		if (hoveredSymbolLine == _prevHoverLine &&
			hoveredSymbolCol >= _prevHoverStartCol &&
			hoveredSymbolCol <= _prevHoverEndCol)
		{
			return;
		}

		// Instantly hide tooltip if mouse goes out of the previous symbol's range
		string lineText = CodeEditor.GetLine(hoveredSymbolLine);
		bool isHoveringValidChar = false;

		if (lineText.Length > 0 && hoveredSymbolCol >= 0 && hoveredSymbolCol < lineText.Length)
		{
			isHoveringValidChar = char.IsLetterOrDigit(lineText[hoveredSymbolCol]) || lineText[hoveredSymbolCol] == '_';
		}

		if (!isHoveringValidChar)
		{
			Tooltip.Visible = false;
			_prevHoverLine = null;
			return;
		}

		// In case the LSP is not ready yet, don't do anything.
		if (_completion == null)
		{
			PT.Print("Code Completion Service is not ready.");
			return;
		}

		// Get the hover info
		CodeHoverContext context = new()
		{
			ScriptPath = Container.TargetFilePathAbsolute,
			Line = hoveredSymbolLine,
			Column = hoveredSymbolCol,
		};

		CodeHoverResult hoverResult = await _completion.GetCodeHoverAsync(context);
		string? tooltipDisplayMD = hoverResult.Contents;

		if (tooltipDisplayMD == null)
		{
			return;
		}

		int startCol = hoveredSymbolCol;
		int endCol = hoveredSymbolCol;

		// Only calculate if the mouse is actually over text, not empty space past the line end
		if (lineText.Length > 0 && hoveredSymbolCol < lineText.Length)
		{
			// Walk backwards from the mouse until we hit a space or symbol,
			// or we reach the beginning
			while (startCol > 0 && (char.IsLetterOrDigit(lineText[startCol]) || lineText[startCol] == '_'))
			{
				startCol--;
			}

			// Walk forwards from the mouse until we hit a space or symbol,
			// or we reach the end
			while (endCol < lineText.Length && (char.IsLetterOrDigit(lineText[endCol]) || lineText[endCol] == '_'))
			{
				endCol++;
			}
		}

		_prevHoverLine = hoveredSymbolLine;
		_prevHoverStartCol = startCol;
		_prevHoverEndCol = endCol;

		string tooltipDisplay = Conversions.MarkdownToBBCode(tooltipDisplayMD);

		Rect2 hoveredCharDimensions = CodeEditor.GetRectAtLineColumn(hoveredSymbolLine, hoveredSymbolCol);
		Rect2 globalCharDimensions = new(CodeEditor.GlobalPosition + hoveredCharDimensions.Position, hoveredCharDimensions.Size);
		await Tooltip.UpdateTooltip(tooltipDisplay, globalCharDimensions);
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
