// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Properties.Components;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using static Polytoria.Datamodel.Data.ColorSeries;
using static Polytoria.Datamodel.Data.NumberSeries;

namespace Polytoria.Creator.Properties;

public sealed partial class NumberSeriesProperty : Control, IProperty<NumberSeries>
{
	private const string NumberSeriesPointPath = "res://scenes/creator/properties/components/number_series/point.tscn";
	private NumberSeries _value;
	[Export] private TextureRect _previewRect = null!;
	[Export] private Button _addButton = null!;
	[Export] private Control _points = null!;

	public NumberSeries Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		Value = (NumberSeries)value!;
	}

	public void Refresh()
	{
		RefreshPreview();
		RefreshList();
	}

	private void RefreshPreview()
	{
		_previewRect.Texture = _value.ToCurveTexture();
	}

	private void NotifyValueChange()
	{
		ValueChanged?.Invoke(Value);
	}

	private void RefreshList()
	{
		ClearPoints();
		ListPoints();
	}

	private void ClearPoints()
	{
		foreach (Node item in _points.GetChildren())
		{
			item.QueueFree();
		}
	}

	private void ListPoints()
	{
		int i = 0;
		foreach (var p in Value.Points)
		{
			int myI = i;
			NumberSeriesPoint ps = Globals.CreateInstanceFromScene<NumberSeriesPoint>(NumberSeriesPointPath);
			ps.Value = p.Value;
			ps.OffsetValue = p.Offset;
			ps.OffsetChanged += (val) =>
			{
				_value.SetOffset(myI, val);
				NotifyValueChange();
				Refresh();
			};
			ps.ValueChanged += (val) =>
			{
				_value.SetValue(myI, val);
				NotifyValueChange();
				RefreshPreview();
			};
			ps.DeleteRequested += () =>
			{
				_value.RemovePoint(myI);
				NotifyValueChange();
				Refresh();
			};
			_points.AddChild(ps);
			i++;
		}
	}

	public override void _Ready()
	{
		_addButton.Pressed += OnAdd;

		Refresh();
	}

	private void OnAdd()
	{
		float newOffset;

		if (_value.Points.Count == 0)
		{
			newOffset = 0.5f;
		}
		else if (_value.Points.Count == 1)
		{
			// add at the opposite end
			float existingOffset = _value.Points[0].Offset;
			newOffset = existingOffset < 0.5f ? 1.0f : 0.0f;
		}
		else
		{
			// Find the largest gap between consecutive points
			float maxGap = 0f;
			float gapStart = 0f;
			float gapEnd = 0f;

			// Sort points by offset to find gaps
			var sortedPoints = new List<NumberPoint>(_value.Points);
			sortedPoints.Sort((a, b) => a.Offset.CompareTo(b.Offset));

			// Check gap from 0 to first point
			if (sortedPoints[0].Offset > 0f)
			{
				maxGap = sortedPoints[0].Offset;
				gapStart = 0f;
				gapEnd = sortedPoints[0].Offset;
			}

			// Check gaps between consecutive points
			for (int i = 0; i < sortedPoints.Count - 1; i++)
			{
				float gap = sortedPoints[i + 1].Offset - sortedPoints[i].Offset;
				if (gap > maxGap)
				{
					maxGap = gap;
					gapStart = sortedPoints[i].Offset;
					gapEnd = sortedPoints[i + 1].Offset;
				}
			}

			// Check gap from last point to 1.0
			if (sortedPoints[^1].Offset < 1.0f)
			{
				float gap = 1.0f - sortedPoints[^1].Offset;
				if (gap > maxGap)
				{
					maxGap = gap;
					gapStart = sortedPoints[^1].Offset;
					gapEnd = 1.0f;
				}
			}

			newOffset = (gapStart + gapEnd) / 2f;
		}

		_value.AddPoint(newOffset, 0f);

		NotifyValueChange();
		Refresh();
	}
}
