// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Chat;

public partial class UIEmojiPicker : Control
{
	private const int EmojiSize = 48;
	private const int MaxRecent = 12;
	private const int ItemWidth = 56;
	private const int GridSeparation = 3;
	private const int GridMargin = 16;
	private const string EmojiPickerItemPath = "res://scenes/client/ui/chat/emoji_picker_item.tscn";

	[Export] private GridContainer _grid = null!;
	[Export] private HBoxContainer _row = null!;
	[Export] private ScrollContainer _gridScroll = null!;
	[Export] private ScrollContainer _rowScroll = null!;

	private PackedScene _itemPacked = null!;

	public event Action<string>? EmojiPicked;
	private readonly List<UIEmojiPickerItem> _gridItems = [];
	private readonly List<UIEmojiPickerItem> _rowItems = [];
	private readonly List<UIEmojiPickerItem> _visibleRowCache = [];
	private int _selectedIndex = -1;
	private string _currentFilter = "";
	private bool _rowPopulated;

	private static readonly List<string> _recentEmojis = [];

	public override void _Ready()
	{
		Visible = false;
	}

	public void Initialize()
	{
		_itemPacked = GD.Load<PackedScene>(EmojiPickerItemPath);
		PopulateItems();
	}

	public void ShowFullPicker(float width)
	{
		CustomMinimumSize = Vector2.Zero;
		_gridScroll.Visible = true;
		_rowScroll.Visible = false;
		RecalculateGridColumns(width);
		FilterByText("");
		_selectedIndex = -1;
	}

	public void ShowAutocomplete(string filter)
	{
		CustomMinimumSize = Vector2.Zero;
		PopulateRowItems();
		_gridScroll.Visible = false;
		_rowScroll.Visible = true;
		FilterByText(filter);
		_selectedIndex = 0;
		UpdateRowSelection();
	}

	public int VisibleItemCount => _visibleRowCache.Count;

	public string GetSelectedEmojiName()
	{
		if (_visibleRowCache.Count == 0) return "";
		if (_selectedIndex < 0 || _selectedIndex >= _visibleRowCache.Count)
			return _visibleRowCache[0].EmojiName;
		return _visibleRowCache[_selectedIndex].EmojiName;
	}

	public void SelectNext()
	{
		if (_visibleRowCache.Count == 0) return;
		_selectedIndex = (_selectedIndex + 1) % _visibleRowCache.Count;
		UpdateRowSelection();
	}

	public void SelectPrev()
	{
		if (_visibleRowCache.Count == 0) return;
		_selectedIndex = (_selectedIndex - 1 + _visibleRowCache.Count) % _visibleRowCache.Count;
		UpdateRowSelection();
	}

	private void UpdateRowSelection()
	{
		for (int i = 0; i < _visibleRowCache.Count; i++)
			_visibleRowCache[i].IsSelected = (i == _selectedIndex);
	}

	private void RecalculateGridColumns(float width)
	{
		float available = width - GridMargin;
		int columns = Mathf.Max(1, Mathf.FloorToInt((available + GridSeparation) / (ItemWidth + GridSeparation)));
		_grid.Columns = columns;
	}

	public void FilterByText(string partialName)
	{
		_currentFilter = partialName;

		if (_gridScroll.Visible)
		{
			foreach (var item in _gridItems)
				item.Visible = string.IsNullOrEmpty(partialName) || item.EmojiName.Contains(partialName, StringComparison.OrdinalIgnoreCase);

			SortWithRecentFirst(_gridItems, _grid);
		}

		if (_rowScroll.Visible)
		{
			foreach (var item in _rowItems)
			{
				bool matches = !string.IsNullOrEmpty(partialName) && item.EmojiName.StartsWith(partialName, StringComparison.OrdinalIgnoreCase);
				item.Visible = string.IsNullOrEmpty(partialName) || matches;
			}

			SortWithRecentFirst(_rowItems, _row);

			_visibleRowCache.Clear();
			int childCount = _row.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				if (_row.GetChild(i) is UIEmojiPickerItem item && item.Visible)
					_visibleRowCache.Add(item);
			}
		}
	}

	private void SortWithRecentFirst(List<UIEmojiPickerItem> items, Container container)
	{
		var recentNames = new HashSet<string>(_recentEmojis);
		int dest = 0;

		for (int i = 0; i < items.Count; i++)
		{
			var item = items[i];
			if (!item.Visible || !recentNames.Contains(item.EmojiName))
				continue;
			if (item.GetIndex() != dest)
				container.MoveChild(item, dest);
			dest++;
		}

		for (int i = 0; i < items.Count; i++)
		{
			var item = items[i];
			if (!item.Visible || recentNames.Contains(item.EmojiName))
				continue;
			if (item.GetIndex() != dest)
				container.MoveChild(item, dest);
			dest++;
		}
	}

	private void PopulateItems()
	{
		foreach ((string name, string path) in ChatService.BuiltInEmojis)
		{
			var item = CreateEmojiItem(name, path);
			_grid.AddChild(item);
			_gridItems.Add(item);
		}
	}

	private void PopulateRowItems()
	{
		if (_rowPopulated)
			return;
		_rowPopulated = true;

		foreach ((string name, string path) in ChatService.BuiltInEmojis)
		{
			var item = CreateEmojiItem(name, path);
			_row.AddChild(item);
			_rowItems.Add(item);
		}
	}

	private UIEmojiPickerItem CreateEmojiItem(string name, string path)
	{
		var item = _itemPacked.Instantiate<UIEmojiPickerItem>();
		item.Pressed += () => OnEmojiSelected(name);
		item.Initialize(name, path, EmojiSize);
		return item;
	}

	public static void RecordEmojiUse(string emojiName)
	{
		_recentEmojis.Remove(emojiName);
		_recentEmojis.Insert(0, emojiName);
		if (_recentEmojis.Count > MaxRecent)
			_recentEmojis.RemoveRange(MaxRecent, _recentEmojis.Count - MaxRecent);
	}

	public static void InsertEmojiAtCursor(LineEdit field, string emojiName)
	{
		if (string.IsNullOrEmpty(emojiName)) return;

		int cursorPos = field.CaretColumn;
		string text = field.Text;

		int lastSpace = text.LastIndexOf(' ', cursorPos - 1);
		int searchFrom = lastSpace >= 0 ? lastSpace : 0;
		int colonIdx = text.LastIndexOf(':', cursorPos - 1);
		int splitPoint = colonIdx >= searchFrom ? colonIdx : cursorPos;

		string before = text[..splitPoint];
		string after = text[cursorPos..];
		string insertion = $":{emojiName}:";

		if (after.Length == 0 || after[0] != ' ')
			insertion += ' ';

		field.Text = before + insertion + after;
		field.CaretColumn = splitPoint + insertion.Length;
	}

	private void OnEmojiSelected(string emojiName)
	{
		RecordEmojiUse(emojiName);
		EmojiPicked?.Invoke(emojiName);
	}
}
