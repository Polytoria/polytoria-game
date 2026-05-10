// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class CompressorEffect : AudioEffectBase
{
	private float _threshold = -20f;
	private float _ratio = 4f;
	private float _gain = 0f;
	private float _attackUs = 20f;
	private float _releaseMs = 250f;

	[Editable, ScriptProperty, DefaultValue(-20f)]
	public float Threshold
	{
		get => _threshold;
		set
		{
			_threshold = value;
			var comp = GetLive<AudioEffectCompressor>();
			if (comp != null) comp.Threshold = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(4f)]
	public float Ratio
	{
		get => _ratio;
		set
		{
			_ratio = value;
			var comp = GetLive<AudioEffectCompressor>();
			if (comp != null) comp.Ratio = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float Gain
	{
		get => _gain;
		set
		{
			_gain = value;
			var comp = GetLive<AudioEffectCompressor>();
			if (comp != null) comp.Gain = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(20f)]
	public float AttackUs
	{
		get => _attackUs;
		set
		{
			_attackUs = value;
			var comp = GetLive<AudioEffectCompressor>();
			if (comp != null) comp.AttackUs = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(250f)]
	public float ReleaseMs
	{
		get => _releaseMs;
		set
		{
			_releaseMs = value;
			var comp = GetLive<AudioEffectCompressor>();
			if (comp != null) comp.ReleaseMs = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectCompressor
		{
			Threshold = _threshold,
			Ratio = _ratio,
			Gain = _gain,
			AttackUs = _attackUs,
			ReleaseMs = _releaseMs
		};
	}
}
