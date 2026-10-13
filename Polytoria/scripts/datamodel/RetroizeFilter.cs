// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class RetroizeFilter : BaseFilter
{
	private int _pixelSize;
	private int _ditherStrength;
	private Vector4I _colorDepth;

	[Editable, ScriptProperty, DefaultValue(3)]
	public int PixelSize
	{
		get => _pixelSize;
		set
		{
			_pixelSize = value;
			SetFilterUniform("pixel_size", _pixelSize);
		}
	}

	[Editable, ScriptProperty, DefaultValue(2)]
	public int DitherStrength
	{
		get => _ditherStrength;
		set
		{
			_ditherStrength = value;
			SetFilterUniform("dither_strength", _ditherStrength);
		}
	}

	[Editable, ScriptProperty, DefaultValue(4)]
	public int RedBitDepth
	{
		get => _colorDepth.X;
		set
		{
			_colorDepth.X = value;
			SetFilterUniform("color_depth", _colorDepth);
		}
	}

	[Editable, ScriptProperty, DefaultValue(4)]
	public int GreenBitDepth
	{
		get => _colorDepth.Y;
		set
		{
			_colorDepth.Y = value;
			SetFilterUniform("color_depth", _colorDepth);
		}
	}

	[Editable, ScriptProperty, DefaultValue(4)]
	public int BlueBitDepth
	{
		get => _colorDepth.Z;
		set
		{
			_colorDepth.Z = value;
			SetFilterUniform("color_depth", _colorDepth);
		}
	}

	[Editable, ScriptProperty, DefaultValue(4)]
	public int AlphaBitDepth
	{
		get => _colorDepth.W;
		set
		{
			_colorDepth.W = value;
			SetFilterUniform("color_depth", _colorDepth);
		}
	}

	protected override Shader LoadFilter() => GD.Load<Shader>("res://resources/shaders/filters/retroize.gdshader");
}
