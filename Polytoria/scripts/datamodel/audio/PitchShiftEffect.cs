// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PitchShiftEffect : AudioEffectBase
{
	private float _pitchScale = 1f;

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float PitchScale
	{
		get => _pitchScale;
		set
		{
			_pitchScale = value;
			var effect = GetLive<AudioEffectPitchShift>();
			if (effect != null) effect.PitchScale = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectPitchShift { PitchScale = _pitchScale };
	}
}
