// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class PosterizeFilter : BaseFilter
{
	protected override Shader _filterShader {
		get => GD.Load<Shader>("res://resources/shaders/filters/posterize.gdshader");
	}

	private int _posterizeLevel;

	[Editable, ScriptProperty, DefaultValue(10)]
	public int PosterizeLevel {
		get => _posterizeLevel;
		set
		{
			_posterizeLevel = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("levels", _posterizeLevel);
	}
}
