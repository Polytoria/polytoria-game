// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class HighPassEffect : AudioEffectBase
{
	private float _cutoffHz = 200f;
	private float _resonance = 0.5f;

	[Editable, ScriptProperty, DefaultValue(200f)]
	public float CutoffHz
	{
		get => _cutoffHz;
		set
		{
			_cutoffHz = value;
			var filter = GetLive<AudioEffectHighPassFilter>();
			if (filter != null) filter.CutoffHz = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Resonance
	{
		get => _resonance;
		set
		{
			_resonance = value;
			var filter = GetLive<AudioEffectHighPassFilter>();
			if (filter != null) filter.Resonance = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectHighPassFilter
		{
			CutoffHz = _cutoffHz,
			Resonance = _resonance
		};
	}
}
