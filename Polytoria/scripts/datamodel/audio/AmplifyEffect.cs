// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class AmplifyEffect : AudioEffectBase
{
	private float _gainDb = 0f;

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float GainDb
	{
		get => _gainDb;
		set
		{
			_gainDb = value;
			var fx = GetLive<AudioEffectAmplify>();
			if (fx != null) fx.VolumeDb = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectAmplify { VolumeDb = _gainDb };
	}
}
