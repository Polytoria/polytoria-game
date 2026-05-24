// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class ChromaticFilter : BaseFilter
{
	protected override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/chromatic.gdshader");
	}

	private int _levels;
	private float _spread;

	[Editable, ScriptProperty, DefaultValue(3)]
	public int Levels
	{
		get => _levels;
		set
		{
			_levels = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.01)]
	public float Spread
	{
		get => _spread;
		set
		{
			_spread = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("levels", _levels);
		_shaderMaterial.SetShaderParameter("spread", _spread);
	}
}
