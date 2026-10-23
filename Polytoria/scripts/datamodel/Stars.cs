// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Static]
public sealed partial class Stars : Instance
{
	private bool _enabled = true;
	private float _density = 0.98f;
	private float _brightness = 1f;
	private float _twinkleSpeed = 1f;
	private Vector3 _rotation;
	private float _horizonPadding;

	[Editable, ScriptProperty]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Density
	{
		get => _density;
		set
		{
			_density = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Brightness
	{
		get => _brightness;
		set
		{
			_brightness = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float TwinkleSpeed
	{
		get => _twinkleSpeed;
		set
		{
			_twinkleSpeed = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 Rotation
	{
		get => _rotation;
		set
		{
			_rotation = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float HorizonPadding
	{
		get => _horizonPadding;
		set
		{
			_horizonPadding = Mathf.Clamp(value, 0f, 0.75f);
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode() => new Node();
}
