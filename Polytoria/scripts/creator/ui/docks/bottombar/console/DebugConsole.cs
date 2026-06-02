// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Polytoria.Scripting.LogDispatcher;

namespace Polytoria.Creator.UI;

public partial class DebugConsole : Control
{
	private const string ErrorColorHex = "#F95D5D";
	private const string WarningColorHex = "#FFBC58";
	private const string ServerColorHex = "#0097FF";
	private const string ClientColorHex = "#F95D5D";
	private const string AddonColorHex = "#4FE883";
	private const string NoneColorHex = "#575757";

	private const int MaxLogLength = 16384;

	private const int FontSizeStep = 2;
	private const int MinFontSize = 8;
	private const int MaxFontSize = 72;

	private readonly StringBuilder _textBuilder = new();

	[Export] private RichTextLabel _richLabel = null!;
	[Export] private LineEdit _searchEdit = null!;
	[Export] private Button _clearBtn = null!;
	[Export] private Button _filterBtn = null!;

	private ConsoleFilters? _consoleFilters;

	public static DebugConsole Singleton { get; private set; } = null!;

	private readonly List<LogData> _logs = [];              // All Logs
	private readonly HashSet<LogData> _shownLogs = [];      // Filtered/Searched Logs
	private string SearchQuery = "";

	// Type Filter
	private readonly HashSet<LogTypeEnum> _typeFilters = [LogTypeEnum.Info, LogTypeEnum.Error, LogTypeEnum.Warning];

	// Source Filter
	private readonly HashSet<LogFromEnum> _sourceFilters = [LogFromEnum.None, LogFromEnum.Client, LogFromEnum.Server, LogFromEnum.Addon];


	// How many logs from the unfiltered list have been rendered
	private int _lastRenderedIndex = 0;
	private bool _needsFullRebuild = false;
	private bool _hasPendingAppend = false;
	private int _currentFontSize = 14;

	private bool IsSearching => !string.IsNullOrEmpty(SearchQuery);

	public DebugConsole()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		VisibilityChanged += OnVisibilityChanged;
		_clearBtn.Pressed += Clear;
		_searchEdit.TextChanged += _ => OnSearch();
		_richLabel.Text = "";

		// ConsoleFilters is a direct child of this Console node
		_consoleFilters = GetNode<ConsoleFilters>("ConsoleFilters");
		if (_consoleFilters != null)
		{
			_filterBtn.Pressed += OnFilterButtonPressed;
		}
		else
		{
			GD.PrintErr("DebugConsole: Could not find ConsoleFilters node!");
		}

		int size = _richLabel.GetThemeFontSize("normal_font_size", "Label");
		_currentFontSize = size > 0 ? size : 16;

	}

	private void OnFilterButtonPressed()
	{
		if (_consoleFilters == null) return;

		if (_consoleFilters.Visible)
		{
			_consoleFilters.Hide();
			return;
		}

		_consoleFilters.Show();

		Vector2 pos = _filterBtn.GlobalPosition;

		_consoleFilters.Position = new Vector2I(
			(int)pos.X,
			(int)(pos.Y - _consoleFilters.Size.Y - 8)
		);
	}

	public override void _Process(double delta)
	{
		if (!IsVisibleInTree())
		{
			base._Process(delta);
			return;
		}

		if (_needsFullRebuild)
			FullRebuild();
		else if (_hasPendingAppend)
			AppendPendingLogs();

		base._Process(delta);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true } mb) return;
		if (!GetGlobalRect().HasPoint(mb.GlobalPosition)) return;

		switch (mb)
		{
			case { CtrlPressed: true, ButtonIndex: MouseButton.WheelUp }:
				_currentFontSize = Mathf.Clamp(_currentFontSize + FontSizeStep, MinFontSize, MaxFontSize);
				_richLabel.AddThemeFontSizeOverride("normal_font_size", _currentFontSize);
				GetViewport().SetInputAsHandled();
				break;
			case { CtrlPressed: true, ButtonIndex: MouseButton.WheelDown }:
				_currentFontSize = Mathf.Clamp(_currentFontSize - FontSizeStep, MinFontSize, MaxFontSize);
				_richLabel.AddThemeFontSizeOverride("normal_font_size", _currentFontSize);
				GetViewport().SetInputAsHandled();
				break;
		}
	}

	private void OnSearch()
	{
		SearchQuery = _searchEdit.Text;
		// Search always requires a full rebuild, filtered view can't be incrementally appended
		ForceFullRebuild();
	}

	public void Clear()
	{
		_logs.Clear();
		_shownLogs.Clear();
		_lastRenderedIndex = 0;
		_needsFullRebuild = false;
		_hasPendingAppend = false;
		_richLabel.Text = "";
	}

	private void Filter()
	{
		ForceFullRebuild();
	}

	// Toggle a log type filter. If all types are disabled, nothing will be filtered.
	public void ToggleTypeFilter(LogTypeEnum type, bool enabled)
	{
		if (enabled)
			_typeFilters.Add(type);
		else
			_typeFilters.Remove(type);

		ForceFullRebuild();
	}

	public bool IsTypeFilterEnabled(LogTypeEnum type)
	{
		return _typeFilters.Contains(type);
	}

	// Toggle a log source filter. If all sources are disabled, nothing will be filtered.
	public void ToggleSourceFilter(LogFromEnum source, bool enabled)
	{
		if (enabled)
			_sourceFilters.Add(source);
		else
			_sourceFilters.Remove(source);

		ForceFullRebuild();
	}

	public bool IsSourceFilterEnabled(LogFromEnum source)
	{
		return _sourceFilters.Contains(source);
	}

	public IEnumerable<LogTypeEnum> GetActiveTypeFilters()
	{
		return _typeFilters;
	}

	public IEnumerable<LogFromEnum> GetActiveSourceFilters()
	{
		return _sourceFilters;
	}

	public void NewLog(LogData data)
	{
		data.LoggedAt = DateTime.Now;




		// Binary search insertion to maintain sorted order
		var index = _logs.BinarySearch(data, Comparer<LogData>.Create((a, b) => a.LoggedAt.CompareTo(b.LoggedAt)));
		if (index < 0) index = ~index;
		_logs.Insert(index, data);

		// Trim old logs if exceeding limit
		if (_logs.Count > MaxLogLength)
		{
			_logs.RemoveAt(0);






			ForceFullRebuild();
			return;
		}

		// Check for active Filter
		if (IsSearching || _typeFilters.Count < 3 || _sourceFilters.Count < 4)
		{
			ForceFullRebuild();

		}




		else
		{
			if (IsVisibleInTree())
				AppendSingleLog(data);
			else
				_hasPendingAppend = true;
		}
	}

	private void OnVisibilityChanged()
	{
		if (!IsVisibleInTree()) return;

		if (_needsFullRebuild)
			FullRebuild();
		else if (_hasPendingAppend)
			AppendPendingLogs();
	}

	private void ForceFullRebuild()
	{
		_needsFullRebuild = true;
		_hasPendingAppend = false;
	}

	private void AppendPendingLogs()
	{
		for (var i = _lastRenderedIndex; i < _logs.Count; i++)
			AppendSingleLog(_logs[i]);

		_hasPendingAppend = false;
	}

	private void AppendSingleLog(LogData item)
	{
		_textBuilder.Clear();
		BuildLogLine(_textBuilder, item);
		_richLabel.AppendText(_textBuilder.ToString());
		_lastRenderedIndex++;
	}

	private void FullRebuild()
	{
		_textBuilder.Clear();

		IEnumerable<LogData> logsToShow = _logs.Where(l =>
		{
			if (!_typeFilters.Contains(l.LogType))
				return false;

			if (!_sourceFilters.Contains(l.LogFrom))
				return false;

			if (!IsSearching)
			{
				return true;
			}

			return (l.Content.Find(SearchQuery, caseSensitive: false) != -1);

		});

		foreach (var item in logsToShow)
			BuildLogLine(_textBuilder, item);

		_richLabel.Text = _textBuilder.ToString();

		_richLabel.ScrollToLine(_richLabel.GetLineCount());

		_lastRenderedIndex = _logs.Count;
		_needsFullRebuild = false;
		_hasPendingAppend = false;
	}

	private static void BuildLogLine(StringBuilder sb, LogData item)
	{
		var dotColor = item.LogFrom switch
		{
			LogFromEnum.None => NoneColorHex,
			LogFromEnum.Client => ClientColorHex,
			LogFromEnum.Server => ServerColorHex,
			LogFromEnum.Addon => AddonColorHex,
			_ => NoneColorHex
		};

		sb.Append("[color=")
			.Append(dotColor)
			.Append("]•[/color] ");

		switch (item.LogType)
		{
			case LogTypeEnum.Info:
				// No color for info logs
				break;
			case LogTypeEnum.Warning:
				sb.Append("[color=").Append(WarningColorHex).Append(']');
				break;
			case LogTypeEnum.Error:
				sb.Append("[color=").Append(ErrorColorHex).Append(']');
				break;
		}

		sb.Append('[')
			.Append(item.LoggedAt.ToLongTimeString())
			.Append("] ")
			.Append(item.Content);

		if (item.LogType != LogTypeEnum.Info)
			sb.Append("[/color]");

		sb.Append('\n');
	}
}
