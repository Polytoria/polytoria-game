// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Data;

public readonly struct NumberSeries : IScriptObject, IData
{
	internal struct NumberPoint(float offset, float value)
	{
		public float Offset = offset;
		public float Value = value;
	}

	private readonly List<NumberPoint> points;

	[ScriptProperty]
	public readonly int PointCount => points?.Count ?? 0;

	internal List<NumberPoint> Points => points;

	public NumberSeries()
	{
		points =
		[
			new(0f, 0f),
			new(1f, 1f)
		];
	}

	public NumberSeries(float min, float max)
	{
		points =
		[
			new(0f, min),
			new(1f, max)
		];
	}

	[ScriptMethod]
	public static NumberSeries New()
	{
		return new();
	}

	[ScriptMethod]
	public static NumberSeries New(float min, float max)
	{
		NumberSeries r = new();
		r.points.Clear();
		r.points.Add(new NumberPoint(0f, min));
		r.points.Add(new NumberPoint(1f, max));
		return r;
	}

	[ScriptMethod]
	public readonly void Clear()
	{
		points.Clear();
	}

	[ScriptMethod]
	public readonly void SetValue(int point, float value)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		points[point] = new NumberPoint(points[point].Offset, value);
	}

	[ScriptMethod]
	public readonly void RemovePoint(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		points.RemoveAt(point);
	}

	[ScriptMethod]
	public readonly float[] GetOffsets()
	{
		List<float> offsets = [];
		foreach (NumberPoint p in points)
		{
			offsets.Add(p.Offset);
		}
		return [.. offsets];
	}

	[ScriptMethod]
	public readonly float[] GetValues()
	{
		List<float> values = [];
		foreach (NumberPoint p in points)
		{
			values.Add(p.Value);
		}
		return [.. values];
	}

	[ScriptMethod]
	public readonly void SetOffset(int point, float offset)
	{
		NumberPoint p = new()
		{
			Offset = offset
		};
		if (point <= 0)
		{
			p.Value = 0f;
			points.Insert(0, p);
		}
		else if (point >= points.Count)
		{
			p.Value = 0f;
			points.Add(p);
		}
		else
		{
			p.Value = points[point].Value;
			points[point] = p;
		}

		SortPoints();
	}

	[ScriptMethod]
	public readonly float GetValue(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		return points[point].Value;
	}

	[ScriptMethod]
	public readonly float GetOffset(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		return points[point].Offset;
	}

	[ScriptMethod]
	public readonly int AddPoint(float offset, float value)
	{
		offset = Math.Clamp(offset, 0f, 1f);
		NumberPoint newPoint = new(offset, value);
		points.Add(newPoint);
		SortPoints();

		// Return the new index after sorting
		for (int i = 0; i < points.Count; i++)
		{
			if (points[i].Offset == offset && points[i].Value == value)
				return i;
		}
		return points.Count - 1;
	}

	[ScriptMethod]
	public readonly float Lerp(float t)
	{
		if (points.Count == 0)
			return 0f;

		if (points.Count == 1)
			return points[0].Value;

		t = Math.Clamp(t, 0f, 1f);

		for (int i = 0; i < points.Count - 1; i++)
		{
			NumberPoint a = points[i];
			NumberPoint b = points[i + 1];

			if (t >= a.Offset && t <= b.Offset)
			{
				float localT = (t - a.Offset) / (b.Offset - a.Offset);
				return Mathf.Lerp(a.Value, b.Value, localT);
			}
		}

		return points[^1].Value;
	}

	private readonly void SortPoints()
	{
		points.Sort((a, b) => a.Offset.CompareTo(b.Offset));
	}
	public override readonly int GetHashCode()
	{
		return HashCode.Combine(points);
	}

	public readonly Curve ToCurve()
	{
		Curve curve = new();

		if (points == null || points.Count == 0)
			return curve;

		foreach (NumberPoint point in points)
			curve.AddPoint(new Vector2(point.Offset, point.Value));

		return curve;
	}

	public readonly CurveTexture ToCurveTexture()
	{
		return new() { Curve = ToCurve() };
	}

	public object Clone()
	{
		NumberSeries n = new();
		n.points.Clear();
		foreach (NumberPoint p in points)
		{
			n.points.Add(p);
		}
		return n;
	}
}
