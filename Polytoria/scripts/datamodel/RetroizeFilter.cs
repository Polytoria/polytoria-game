// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class RetroizeFilter : BaseFilter
{
	internal override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/retroize.gdshader");
	}

	private int _pixelSize;
	private int _bayerResolution;
	private int _brightnessLevels;

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

	[Editable, ScriptProperty, DefaultValue(2)]
	public int BayerResolution
	{
		get => _bayerResolution;
		set
		{
			_bayerResolution = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty, DefaultValue(8)]
	public int BrightnessLevels
	{
		get => _brightnessLevels;
		set
		{
			_brightnessLevels = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("pixel_size", _pixelSize);
		_shaderMaterial.SetShaderParameter("bayer_resolution", _bayerResolution);
		_shaderMaterial.SetShaderParameter("brightness_levels", _brightnessLevels);
		base.UpdateFilter();
	}
}
