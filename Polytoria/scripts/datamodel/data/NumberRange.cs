// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;
using System;

namespace Polytoria.Datamodel.Data;

public struct NumberRange : IScriptObject, IData
{
	private float _min = 0;
	private float _max = 0;

	[ScriptProperty]
	public float Min
	{
		readonly get => _min;
		set
		{
			_min = value;
			if (_min > _max)
			{
				_max = _min;
			}
		}
	}

	[ScriptProperty]
	public float Max
	{
		readonly get => _max;
		set
		{
			_max = value;
			if (_max < _min)
			{
				_min = _max;
			}
		}
	}

	public NumberRange() { }

	public NumberRange(float min, float max)
	{
		_min = min;
		_max = max;
	}

	[ScriptMethod]
	public static NumberRange New(float from, float to)
	{
		return new() { Min = from, Max = to };
	}

	[ScriptMethod]
	public readonly float Lerp(float t)
	{
		return Mathf.Lerp(Min, Max, t);
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Min, Max);
	}

	public object Clone()
	{
		return new NumberRange() { Min = Min, Max = Max };
	}
}
