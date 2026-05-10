// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class DistortionEffect : AudioEffectBase
{
	private float _drive = 0.5f;
	private float _preGain = 0f;
	private float _postGain = 0f;

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Drive
	{
		get => _drive;
		set
		{
			_drive = value;
			var dist = GetLive<AudioEffectDistortion>();
			if (dist != null) dist.Drive = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float PreGain
	{
		get => _preGain;
		set
		{
			_preGain = value;
			var dist = GetLive<AudioEffectDistortion>();
			if (dist != null) dist.PreGain = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float PostGain
	{
		get => _postGain;
		set
		{
			_postGain = value;
			var dist = GetLive<AudioEffectDistortion>();
			if (dist != null) dist.PostGain = value;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectDistortion
		{
			Drive = _drive,
			PreGain = _preGain,
			PostGain = _postGain
		};
	}
}
