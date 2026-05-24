// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class HueShiftFilter : BaseFilter
{
	protected override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/hueshift.gdshader");
	}

	private float _hueShift;

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float HueShift
	{
		get => _hueShift;
		set
		{
			_hueShift = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("shift", _hueShift);
	}
}
