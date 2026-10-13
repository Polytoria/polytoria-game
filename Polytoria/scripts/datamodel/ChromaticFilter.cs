// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class ChromaticFilter : BaseFilter
{
	private int _levels;
	private float _spread;

	[Editable, ScriptProperty, DefaultValue(3)]
	public int Levels
	{
		get => _levels;
		set
		{
			_levels = value;
			SetFilterUniform("levels", _levels);
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.01)]
	public float Spread
	{
		get => _spread;
		set
		{
			_spread = value;
			SetFilterUniform("spread", _spread);
		}
	}

	protected override Shader LoadFilter() => GD.Load<Shader>("res://resources/shaders/filters/chromatic.gdshader");
}
