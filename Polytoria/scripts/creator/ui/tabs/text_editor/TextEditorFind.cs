// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.UI.TextEditor;

public sealed partial class TextEditorFind : Control
{
	[Export] private LineEdit _searchField = null!;
	[Export] private LineEdit _replaceField = null!;
	[Export] private Button _prevButton = null!;
	[Export] private Button _nextButton = null!;
	[Export] private Button _closeButton = null!;
	[Export] private Button _replaceButton = null!;
	[Export] private Button _replaceAllButton = null!;
	[Export] private Label _searchIndexLabel = null!;
	[Export] private BaseButton _caseSenstiveCheck = null!;
	[Export] private BaseButton _wholeWordCheck = null!;

	private readonly List<Vector2I> _searchResults = [];
	private int _currentResultIndex = -1;

	public TextEditorRoot Root = null!;

	public bool Active = false;

	public override void _Ready()
	{
		_closeButton.Pressed += Close;
		Visible = false;
		_searchField.TextChanged += OnSearchChanged;
		_searchField.TextSubmitted += (_) => FindNext();
		_replaceField.TextSubmitted += (_) => ReplaceCurrentMatch();
		_replaceField.TextSubmitted += (_) => ReplaceCurrentMatch();
		_nextButton.Pressed += FindNext;
		_prevButton.Pressed += FindPrevious;
		_replaceButton.Pressed += ReplaceCurrentMatch;
		_replaceAllButton.Pressed += ReplaceAll;
		_caseSenstiveCheck.Pressed += SearchCurrent;
		_wholeWordCheck.Pressed += SearchCurrent;
		base._Ready();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape && Active)
			Close();
		base._Input(@event);
	}

	private void OnSearchFieldInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed)
		{
			if (key.Keycode == Key.Enter)
			{
				FindNext();
			}
		}
	}

	private void OnReplaceFieldInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed)
		{
			if (key.Keycode == Key.Enter)
			{
				ReplaceCurrentMatch();
			}
		}
	}

	private void OnSearchChanged(string newText)
	{
		OnSearch(newText);
	}

	private void SearchCurrent()
	{
		if (!Active) return;
		OnSearch(_searchField.Text);
	}

	public void OnSearch(string str)
	{
		_searchResults.Clear();
		_currentResultIndex = -1;

		if (string.IsNullOrEmpty(str))
		{
			_closeButton.Text = "";
			return;
		}

		FindAllMatches(str);

		if (_searchResults.Count > 0)
		{
			_currentResultIndex = 0;
			JumpToResult(0);
		}

		UpdateResultLabel();
	}

	private void FindAllMatches(string searchText)
	{
		StringComparison comparison = _caseSenstiveCheck.ButtonPressed
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

		int lineCount = Root.CodeEditor.GetLineCount();

		for (int line = 0; line < lineCount; line++)
		{
			string lineText = Root.CodeEditor.GetLine(line);
			int index = 0;

			while (index < lineText.Length)
			{
				index = lineText.IndexOf(searchText, index, comparison);
				if (index == -1) break;

				// Check whole word
				if (_wholeWordCheck.ButtonPressed)
				{
					bool validStart = index == 0 || !IsWordChar(lineText[index - 1]);
					bool validEnd = index + searchText.Length >= lineText.Length ||
								   !IsWordChar(lineText[index + searchText.Length]);

					if (!validStart || !validEnd)
					{
						index++;
						continue;
					}
				}

				_searchResults.Add(new(line, index));
				index += searchText.Length;
			}
		}
	}

	private void FindNext()
	{
		if (_searchResults.Count == 0) return;

		_currentResultIndex = (_currentResultIndex + 1) % _searchResults.Count;
		JumpToResult(_currentResultIndex);
		UpdateResultLabel();
	}

	private void FindPrevious()
	{
		if (_searchResults.Count == 0) return;

		_currentResultIndex--;
		if (_currentResultIndex < 0)
			_currentResultIndex = _searchResults.Count - 1;

		JumpToResult(_currentResultIndex);
		UpdateResultLabel();
	}

	private static bool IsWordChar(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_';
	}

	private void JumpToResult(int index)
	{
		if (index < 0 || index >= _searchResults.Count) return;

		var result = _searchResults[index];
		int line = result.X;
		int column = result.Y;

		Root.CodeEditor.Select(line, column, line, column + _searchField.Text.Length);
		Root.CodeEditor.CenterViewportToCaret();
	}

	private void UpdateResultLabel()
	{
		if (_searchResults.Count == 0)
		{
			_searchIndexLabel.Text = "No results";
		}
		else
		{
			_searchIndexLabel.Text = $"{_currentResultIndex + 1} of {_searchResults.Count}";
		}
	}

	private void ReplaceCurrentMatch()
	{
		if (_currentResultIndex < 0 || _currentResultIndex >= _searchResults.Count) return;
		if (string.IsNullOrEmpty(_searchField.Text)) return;

		var result = _searchResults[_currentResultIndex];
		int line = result.X;
		int column = result.Y;

		string lineText = Root.CodeEditor.GetLine(line);
		string newLineText = lineText.Remove(column, _searchField.Text.Length)
									 .Insert(column, _replaceField.Text);
		Root.CodeEditor.SetLine(line, newLineText);

		SearchCurrent();

		if (_searchResults.Count > 0 && _currentResultIndex < _searchResults.Count)
		{
			JumpToResult(_currentResultIndex);
		}
	}

	private void ReplaceAll()
	{
		if (_searchResults.Count == 0) return;
		if (string.IsNullOrEmpty(_searchField.Text)) return;

		// Replace from bottom to top to maintain indices
		for (int i = _searchResults.Count - 1; i >= 0; i--)
		{
			var result = _searchResults[i];
			int line = result.X;
			int column = result.Y;

			string lineText = Root.CodeEditor.GetLine(line);
			string newLineText = lineText.Remove(column, _searchField.Text.Length)
										 .Insert(column, _replaceField.Text);
			Root.CodeEditor.SetLine(line, newLineText);
		}

		// Refresh search results
		SearchCurrent();
	}


	public void Open(string curText = "")
	{
		Active = true;
		Visible = true;
		_searchField.GrabFocus();

		if (!string.IsNullOrEmpty(curText))
		{
			_searchField.Text = curText;
		}
		SearchCurrent();
	}

	public void Close()
	{
		Active = false;
		Visible = false;
		Root.CodeEditor.GrabFocus();
	}
}
