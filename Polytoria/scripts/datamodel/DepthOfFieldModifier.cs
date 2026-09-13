// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class DepthOfFieldModifier : LightingModifier
{
	private Vector2 _distance = new(2f, 100f);
	private Vector2 _transition = new(1f, 10f);
	private float _amount = 0.1f;

	[Editable, ScriptProperty]
	public Vector2 Distance
	{
		get => _distance;
		set
		{
			_distance = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Transition
	{
		get => _transition;
		set
		{
			_transition = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Amount
	{
		get => _amount;
		set
		{
			_amount = value;
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
		Root.Lighting.cameraAttributes.DofBlurFarEnabled = false;
		Root.Lighting.cameraAttributes.DofBlurNearEnabled = false;
		base.PreDelete();
	}

	private void ApplyEffects()
	{
		var attrs = Root.Lighting.cameraAttributes;
		if (IsHidden)
		{
			attrs.DofBlurFarEnabled = false;
			attrs.DofBlurNearEnabled = false;
			return;
		}
		attrs.DofBlurFarEnabled = true;
		attrs.DofBlurNearDistance = _distance.X;
		attrs.DofBlurFarDistance = _distance.Y;
		attrs.DofBlurNearTransition = _transition.X;
		attrs.DofBlurFarTransition = _transition.Y;
		attrs.DofBlurNearEnabled = true;
		attrs.DofBlurAmount = _amount;
	}

	public override void HiddenChanged(bool to)
	{
		ApplyEffects();
		base.HiddenChanged(to);
	}
}
