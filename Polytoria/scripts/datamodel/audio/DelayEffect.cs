// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class DelayEffect : AudioEffectBase
{
	private float _wet = 0.5f;
	private float _dry = 1f;
	private float _feedbackPercent = 50f;
	private float _delayMs = 250f;

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Wet
	{
		get => _wet;
		set
		{
			_wet = value;
			var delay = GetLive<AudioEffectDelay>();
			if (delay != null) delay.Tap1LevelDb = Mathf.LinearToDb(value);
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
			var delay = GetLive<AudioEffectDelay>();
			if (delay != null) delay.Dry = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(50f)]
	public float FeedbackPercent
	{
		get => _feedbackPercent;
		set
		{
			_feedbackPercent = value;
			var delay = GetLive<AudioEffectDelay>();
			if (delay != null) delay.FeedbackLevelDb = Mathf.LinearToDb(value / 100f);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(250f)]
	public float DelayMs
	{
		get => _delayMs;
		set
		{
			_delayMs = value;
			var delay = GetLive<AudioEffectDelay>();
			if (delay != null) delay.Tap1DelayMs = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectDelay
		{
			Dry = _dry,
			Tap1Active = true,
			Tap1DelayMs = _delayMs,
			Tap1LevelDb = Mathf.LinearToDb(_wet),
			FeedbackActive = true,
			FeedbackLevelDb = Mathf.LinearToDb(_feedbackPercent / 100f)
		};
	}
}
