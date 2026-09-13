// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BloomModifier : LightingModifier
{
	private float _intensity = 1f;
	private float _bloom;
	private float _threshold = 1f;

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Intensity
	{
		get => _intensity;
		set
		{
			_intensity = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Bloom
	{
		get => _bloom;
		set
		{
			_bloom = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Threshold
	{
		get => _threshold;
		set
		{
			_threshold = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}


	public override void Init()
	{
		base.Init();
		ApplyEffects();
	}

	public override void PreDelete()
	{
		Root.Lighting.environment.GlowEnabled = false;
		base.PreDelete();
	}

	private void ApplyEffects()
	{
		if (IsHidden)
		{
			Root.Lighting.environment.GlowEnabled = false;
			return;
		}
		Root.Lighting.environment.GlowEnabled = true;
		Root.Lighting.environment.GlowIntensity = _intensity;
		Root.Lighting.environment.GlowBloom = _bloom;
		Root.Lighting.environment.GlowHdrThreshold = _threshold;
	}

	public override void HiddenChanged(bool to)
	{
		ApplyEffects();
		base.HiddenChanged(to);
	}
}
