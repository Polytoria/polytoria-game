// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ReverbEffect : AudioEffectBase
{
	private float _roomSize = 0.8f;
	private float _damping = 0.5f;
	private float _wet = 0.5f;
	private float _dry = 1f;
	private float _spread = 1f;

	[Editable, ScriptProperty, DefaultValue(0.8f)]
	public float RoomSize
	{
		get => _roomSize;
		set
		{
			_roomSize = value;
			var reverb = GetLive<AudioEffectReverb>();
			if (reverb != null) reverb.RoomSize = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Damping
	{
		get => _damping;
		set
		{
			_damping = value;
			var reverb = GetLive<AudioEffectReverb>();
			if (reverb != null) reverb.Damping = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Wet
	{
		get => _wet;
		set
		{
			_wet = value;
			var reverb = GetLive<AudioEffectReverb>();
			if (reverb != null) reverb.Wet = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Dry
	{
		get => _dry;
		set
		{
			_dry = value;
			var reverb = GetLive<AudioEffectReverb>();
			if (reverb != null) reverb.Dry = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Spread
	{
		get => _spread;
		set
		{
			_spread = value;
			var reverb = GetLive<AudioEffectReverb>();
			if (reverb != null) reverb.Spread = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectReverb
		{
			RoomSize = _roomSize,
			Damping = _damping,
			Wet = _wet,
			Dry = _dry,
			Spread = _spread
		};
	}
}
