// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;
using System;

namespace Polytoria.Datamodel.Data;

public struct UIScale : IScriptObject, IData
{
	[ScriptProperty]
	public float Offset { get; set; }

	[ScriptProperty]
	public float Scale { get; set; }

	public UIScale() { }

	public UIScale(float offset, float scale)
	{
		Offset = offset;
		Scale = scale;
	}

	[ScriptMethod]
	public readonly float Compute(float parentSize)
	{
		return Mathf.Max(0, Offset + Scale * parentSize);
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Offset, Scale);
	}

	object IData.Clone() => new UIScale(Offset, Scale);
}
