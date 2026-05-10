// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class LimiterEffect : AudioEffectBase
{
	private float _ceilingDb = -0.1f;

	[Editable, ScriptProperty, DefaultValue(-0.1f)]
	public float CeilingDb
	{
		get => _ceilingDb;
		set
		{
			_ceilingDb = value;
			var fx = GetLive<AudioEffectHardLimiter>();
			if (fx != null) fx.CeilingDb = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectHardLimiter { CeilingDb = _ceilingDb };
	}
}
