// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class VignetteFilter : BaseFilter
{
	private float _innerRadius = 0.0f;
	private float _outerRadius = 1.0f;
	private Vector2 _offset = new(0.5f, 0.5f);
	private Color _color = new(0, 0, 0);

	[Editable, ScriptProperty, DefaultValue(0.0)]
	public float InnerRadius
	{
		get => _innerRadius;
		set
		{
			_innerRadius = value;
			SetFilterUniform("inner_radius", _innerRadius);
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.0)]
	public float OuterRadius
	{
		get => _outerRadius;
		set
		{
			_outerRadius = value;
			SetFilterUniform("outer_radius", _outerRadius);
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Offset
	{
		get => _offset;
		set
		{
			_offset = value;
			SetFilterUniform("offset", _offset);
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			SetFilterUniform("color", _color);
		}
	}

	protected override Shader LoadFilter() => GD.Load<Shader>("res://resources/shaders/filters/vignette.gdshader");
}
