// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class PosterizeFilter : BaseFilter
{
	private Vector3I _posterizeLevels = new(0, 0, 10);

	[Editable, ScriptProperty, DefaultValue(0)]
	public int HueLevels
	{
		get => _posterizeLevels.X;
		set
		{
			_posterizeLevels.X = value;
			SetFilterUniform("levels", _posterizeLevels);
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public int SaturationLevels
	{
		get => _posterizeLevels.Y;
		set
		{
			_posterizeLevels.Y = value;
			SetFilterUniform("levels", _posterizeLevels);
		}
	}

	[Editable, ScriptProperty, DefaultValue(10)]
	public int ValueLevels
	{
		get => _posterizeLevels.Z;
		set
		{
			_posterizeLevels.Z = value;
			SetFilterUniform("levels", _posterizeLevels);
		}
	}

	protected override Shader LoadFilter() => GD.Load<Shader>("res://resources/shaders/filters/posterize.gdshader");
}
