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
	public const string ErrorColorHex = "#F95D5D";
	public const string WarningColorHex = "#FFBC58";
	public const string ServerColorHex = "#0097FF";
	public const string ClientColorHex = "#F95D5D";
	public const string AddonColorHex = "#4FE883";
	public const string NoneColorHex = "#575757";

	private const int MaxLogLength = 16384;

	private const int FontSizeStep = 2;
	private const int MinFontSize = 8;
	private const int MaxFontSize = 72;
	
	private readonly StringBuilder _textBuilder = new();

	[Export] private RichTextLabel _richLabel = null!;
	[Export] private LineEdit _searchEdit = null!;
	[Export] private Button _clearBtn = null!;

	public static DebugConsole Singleton { get; private set; } = null!;

	public List<LogData> Logs = [];
	public HashSet<LogData> ShownLogs = [];
	public string SearchQuery = "";

	// How many logs from the unfiltered list have been rendered
	private int _lastRenderedIndex = 0;
	private bool _needsFullRebuild = false;
	private bool _hasPendingAppend = false;
	private int _currentFontSize = 14;

	private bool IsFiltering => !string.IsNullOrEmpty(SearchQuery);

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
		int size = _richLabel.GetThemeFontSize("normal_font_size", "Label");
    	_currentFontSize = size > 0 ? size : 16;
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
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (!GetGlobalRect().HasPoint(mb.GlobalPosition)) return;
			
			if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelUp)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize + FontSizeStep, MinFontSize, MaxFontSize);
				_richLabel.AddThemeFontSizeOverride("normal_font_size", _currentFontSize);
				GetViewport().SetInputAsHandled();
			}
			else if (mb.CtrlPressed && mb.ButtonIndex == MouseButton.WheelDown)
			{
				_currentFontSize = Mathf.Clamp(_currentFontSize - FontSizeStep, MinFontSize, MaxFontSize);
				_richLabel.AddThemeFontSizeOverride("normal_font_size", _currentFontSize);
				GetViewport().SetInputAsHandled();
			}
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
		Logs.Clear();
		ShownLogs.Clear();
		_lastRenderedIndex = 0;
		_needsFullRebuild = false;
		_hasPendingAppend = false;
		_richLabel.Text = "";
	}

	public void NewLog(LogData data)
	{
		data.LoggedAt = DateTime.Now;
		if (ShownLogs.Contains(data)) return;

		ShownLogs.Add(data);

		// Binary search insertion to maintain sorted order
		int index = Logs.BinarySearch(data, Comparer<LogData>.Create((a, b) => a.LoggedAt.CompareTo(b.LoggedAt)));
		if (index < 0) index = ~index;
		Logs.Insert(index, data);

		// Trim old logs if exceeding limit
		if (Logs.Count > MaxLogLength)
		{
			int removeCount = Logs.Count - MaxLogLength;
			for (int i = 0; i < removeCount; i++)
			{
				ShownLogs.Remove(Logs[0]);
				Logs.RemoveAt(0);
			}

			ForceFullRebuild();
			return;
		}

		// Out-of-order insertion or active search filter, must full rebuild
		if (index < Logs.Count - 1 || IsFiltering)
		{
			ForceFullRebuild();
			return;
		}

		// Fast path: new log at the end, no filter active
		if (IsVisibleInTree())
			AppendSingleLog(data);
		else
			_hasPendingAppend = true;
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
		for (int i = _lastRenderedIndex; i < Logs.Count; i++)
			AppendSingleLog(Logs[i]);

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

		IEnumerable<LogData> logsToShow = IsFiltering
			? Logs.Where(l => l.Content.Find(SearchQuery, caseSensitive: false) != -1)
			: Logs;

		foreach (LogData item in logsToShow)
			BuildLogLine(_textBuilder, item);

		_richLabel.Text = _textBuilder.ToString();
		_lastRenderedIndex = Logs.Count;
		_needsFullRebuild = false;
		_hasPendingAppend = false;
	}

	private static void BuildLogLine(StringBuilder sb, LogData item)
	{
		string dotColor = item.LogFrom switch
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

		if (item.LogType == LogTypeEnum.Warning)
			sb.Append("[color=").Append(WarningColorHex).Append(']');
		else if (item.LogType == LogTypeEnum.Error)
			sb.Append("[color=").Append(ErrorColorHex).Append(']');

		sb.Append('[')
			.Append(item.LoggedAt.ToLongTimeString())
			.Append("] ")
			.Append(item.Content);

		if (item.LogType != LogTypeEnum.Info)
			sb.Append("[/color]");

		sb.Append('\n');
	}
}
