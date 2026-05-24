// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BlurFilter : BaseFilter
{
	protected override Shader _filterShader {
		get => GD.Load<Shader>("res://resources/shaders/filters/blur.gdshader");
	}

	private float _blurStrength;

	[Editable, ScriptProperty, DefaultValue(0.05)]
	public float BlurStrength {
		get => _blurStrength;
		set
		{
			_blurStrength = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("blur_strength", _blurStrength);
	}
}
