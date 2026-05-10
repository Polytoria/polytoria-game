// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PhaserEffect : AudioEffectBase
{
	private float _rangeMinHz = 440f;
	private float _rangeMaxHz = 1600f;
	private float _rateHz = 0.5f;
	private float _feedback = 0.7f;
	private float _depth = 1f;

	[Editable, ScriptProperty, DefaultValue(440f)]
	public float RangeMinHz
	{
		get => _rangeMinHz;
		set
		{
			_rangeMinHz = value;
			var phaser = GetLive<AudioEffectPhaser>();
			if (phaser != null) phaser.RangeMinHz = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1600f)]
	public float RangeMaxHz
	{
		get => _rangeMaxHz;
		set
		{
			_rangeMaxHz = value;
			var phaser = GetLive<AudioEffectPhaser>();
			if (phaser != null) phaser.RangeMaxHz = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float RateHz
	{
		get => _rateHz;
		set
		{
			_rateHz = value;
			var phaser = GetLive<AudioEffectPhaser>();
			if (phaser != null) phaser.RateHz = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.7f)]
	public float Feedback
	{
		get => _feedback;
		set
		{
			_feedback = value;
			var phaser = GetLive<AudioEffectPhaser>();
			if (phaser != null) phaser.Feedback = value;
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
			var phaser = GetLive<AudioEffectPhaser>();
			if (phaser != null) phaser.Depth = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectPhaser
		{
			RangeMinHz = _rangeMinHz,
			RangeMaxHz = _rangeMaxHz,
			RateHz = _rateHz,
			Feedback = _feedback,
			Depth = _depth
		};
	}
}
