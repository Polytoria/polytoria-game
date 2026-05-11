// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.TextEditor;

public sealed partial class TextEditorField : CodeEdit
{
	private const int FontSizeStep = 2;
	private const int MinFontSize = 8;
	private const int MaxFontSize = 72;

	public TextEditorRoot Root = null!;

	private int _currentFontSize = 16;

	public override void _Ready()
	{
		int size = GetThemeFontSize("font_size", "Label");
		_currentFontSize = size > 0 ? size : 16;
		base._Ready();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelUp)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize + FontSizeStep, MinFontSize, MaxFontSize);
				AddThemeFontSizeOverride("font_size", _currentFontSize);
				AcceptEvent();
			}
			else if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelDown)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize - FontSizeStep, MinFontSize, MaxFontSize);
				AddThemeFontSizeOverride("font_size", _currentFontSize);
				AcceptEvent();
			}
		}

		base._GuiInput(@event);
	}

	public override void _ConfirmCodeCompletion(bool replace)
	{
		int index = GetCodeCompletionSelectedIndex();
		if (index == -1) return;

		var selectedOption = GetCodeCompletionOption(index);
		string insertText = (string)selectedOption["insert_text"];

		// Referenced from https://github.com/godotengine/godot/blob/c742d107e29b2c858ef8930760479deb413c68bc/scene/gui/code_edit.cpp#L2367C16-L2367C39
		string completionBase = GetCompletionPrefix();

		int line = GetCaretLine();
		int column = GetCaretColumn();

		BeginComplexOperation();

		if (replace)
		{
			string lineText = GetLine(line);
			int caretCol = column;
			int caretRemoveLine = line;
			bool mergeText = true;

			if (IsInString(line, column) != -1)
			{
				Vector2I stringEnd = (Vector2I)GetDelimiterEndPosition(line, column);
				if (stringEnd.X != -1)
				{
					mergeText = false;
					caretRemoveLine = stringEnd.Y;
					caretCol = stringEnd.X;
				}
			}

			if (mergeText)
			{
				while (caretCol < lineText.Length && !IsSymbol(lineText[caretCol]))
				{
					caretCol++;
				}
			}

			RemoveText(line, column - completionBase.Length, caretRemoveLine, caretCol);
			InsertTextAtCaret(insertText);
		}
		else
		{
			string lineText = GetLine(line);
			int caretCol = column;
			int matchingChars = completionBase.Length;

			while (matchingChars < insertText.Length)
			{
				if (caretCol >= lineText.Length || lineText[caretCol] != insertText[matchingChars])
					break;

				caretCol++;
				matchingChars++;
			}

			RemoveText(line, column - completionBase.Length, line, column);
			InsertTextAtCaret(insertText[..completionBase.Length]);
			SetCaretColumn(caretCol);
			InsertTextAtCaret(insertText[matchingChars..]);
		}

		// Handle parentheses
		if (insertText.EndsWith("()"))
		{
			SetCaretColumn(GetCaretColumn() - 1);
		}

		EndComplexOperation();
		CancelCodeCompletion();
	}

	private string GetCompletionPrefix()
	{
		string lineText = GetLine(GetCaretLine());
		int column = GetCaretColumn();
		int start = column;

		while (start > 0 && !IsSymbol(lineText[start - 1]) && !char.IsWhiteSpace(lineText[start - 1]))
		{
			start--;
		}

		return lineText[start..column];
	}

	private static bool IsSymbol(char c)
	{
		return "!\"#$%&'()*+,-./:;<=>?@[\\]^`{|}~".Contains(c);
	}
}
