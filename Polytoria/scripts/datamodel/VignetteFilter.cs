// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class VignetteFilter : BaseFilter
{
	internal override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/vignette.gdshader");
	}

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
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1.0)]
	public float OuterRadius
	{
		get => _outerRadius;
		set
		{
			_outerRadius = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Offset
	{
		get => _offset;
		set
		{
			_offset = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("inner_radius", _innerRadius);
		_shaderMaterial.SetShaderParameter("outer_radius", _outerRadius);
		_shaderMaterial.SetShaderParameter("offset", _offset);
		_shaderMaterial.SetShaderParameter("color", _color);
		base.UpdateFilter();
	}
}
