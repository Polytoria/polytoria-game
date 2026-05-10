// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ChorusEffect : AudioEffectBase
{
	private float _wet = 0.5f;
	private float _dry = 1f;
	private float _depth = 1f;
	private float _speed = 0.5f;

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Wet
	{
		get => _wet;
		set
		{
			_wet = value;
			var chorus = GetLive<AudioEffectChorus>();
			if (chorus != null) chorus.Wet = value;
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
			var chorus = GetLive<AudioEffectChorus>();
			if (chorus != null) chorus.Dry = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Depth
	{
		get => _depth;
		set
		{
			_depth = value;
			var chorus = GetLive<AudioEffectChorus>();
			if (chorus != null) chorus.SetVoiceDepthMs(0, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Speed
	{
		get => _speed;
		set
		{
			_speed = value;
			var chorus = GetLive<AudioEffectChorus>();
			if (chorus != null) chorus.SetVoiceRateHz(0, value);
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		AudioEffectChorus chorus = new() { Wet = _wet, Dry = _dry };
		chorus.SetVoiceDepthMs(0, _depth);
		chorus.SetVoiceRateHz(0, _speed);
		return chorus;
	}
}
