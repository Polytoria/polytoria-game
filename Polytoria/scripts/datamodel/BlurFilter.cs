// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BlurFilter : BaseFilter
{
	private float _blurStrength;

	[Editable, ScriptProperty, DefaultValue(0.05)]
	public float BlurStrength
	{
		get => _blurStrength;
		set
		{
			_blurStrength = value;
			SetFilterUniform("blur_strength", _blurStrength);
		}
	}

	protected override Shader LoadFilter() => GD.Load<Shader>("res://resources/shaders/filters/blur.gdshader");
}
