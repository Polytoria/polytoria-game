// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class PosterizeFilter : BaseFilter
{
	internal override Shader _filterShader
	{
		get => GD.Load<Shader>("res://resources/shaders/filters/posterize.gdshader");
	}

	private Vector3I _posterizeLevels = new(0, 0, 10);

	[Editable, ScriptProperty, DefaultValue(0)]
	public int HueLevels
	{
		get => _posterizeLevels.X;
		set
		{
			_posterizeLevels.X = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public int SaturationLevels
	{
		get => _posterizeLevels.Y;
		set
		{
			_posterizeLevels.Y = value;
			UpdateFilter();
		}
	}

	[Editable, ScriptProperty, DefaultValue(10)]
	public int ValueLevels
	{
		get => _posterizeLevels.Z;
		set
		{
			_posterizeLevels.Z = value;
			UpdateFilter();
		}
	}

	protected override void UpdateFilter()
	{
		_shaderMaterial.SetShaderParameter("levels", _posterizeLevels);
	}
}
