// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class AdaptiveExposureModifier : LightingModifier
{
	private bool _enabled;
	private float _scale = 1.0f;
	private float _speed = 0.5f;
	private NumberRange _sensitivity = new(50f, 800f);

	[Editable, ScriptProperty]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Scale
	{
		get => _scale;
		set
		{
			_scale = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Speed
	{
		get => _speed;
		set
		{
			_speed = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange Sensitivity
	{
		get => _sensitivity;
		set
		{
			_sensitivity = value;
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
		Root.Lighting.cameraAttributes.AutoExposureEnabled = false;
		base.PreDelete();
	}

	private void ApplyEffects()
	{
		var attrs = Root.Lighting.cameraAttributes;
		if (IsHidden)
		{
			attrs.AutoExposureEnabled = false;
			return;
		}
		attrs.AutoExposureEnabled = _enabled;
		attrs.AutoExposureScale = _scale;
		attrs.AutoExposureSpeed = _speed;
		attrs.AutoExposureMinSensitivity = _sensitivity.Min;
		attrs.AutoExposureMaxSensitivity = _sensitivity.Max;
	}

	public override void HiddenChanged(bool to)
	{
		ApplyEffects();
		base.HiddenChanged(to);
	}
}
