// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class PixelFilter : BaseFilter
{
	internal override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/pixelate.gdshader");
	}

	private int _pixelSize;

	[Editable, ScriptProperty, DefaultValue(3)]
	public int PixelSize
	{
		get => _pixelSize;
		set
		{
			_pixelSize = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("pixel_size", _pixelSize);
	}
}
