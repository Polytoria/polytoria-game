// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;

namespace Polytoria.Datamodel.Data;

[ScriptEnum]
public enum ShadowBlendMode
{
	Normal,
	Add,
	Subtract,
}

[MemoryPackable]
public partial struct ShadowLayer : IScriptObject, IData
{
	private float _radius;

	[MemoryPackAllowSerialize]
	[ScriptProperty]
	public Color Color { get; set; }

	[MemoryPackAllowSerialize]
	[ScriptProperty]
	public Vector2 Offset { get; set; }

	[ScriptProperty]
	public float Radius
	{
		get => _radius;
		set => _radius = Mathf.Max(0, value);
	}

	private float _spread;

	[ScriptProperty]
	public float Spread
	{
		get => _spread;
		set => _spread = Mathf.Max(0, value);
	}

	[ScriptProperty]
	public ShadowBlendMode BlendMode { get; set; }

	public ShadowLayer()
	{
		Color = new Color(0, 0, 0, 0.2f);
		Offset = new Vector2(0, 4);
		_radius = 8f;
		Spread = 0f;
		BlendMode = ShadowBlendMode.Normal;
	}

	object IData.Clone() => new ShadowLayer
	{
		Color = Color,
		Offset = Offset,
		Radius = Radius,
		Spread = Spread,
		BlendMode = BlendMode,
	};
}
