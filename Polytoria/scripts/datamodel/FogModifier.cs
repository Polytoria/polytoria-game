// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class FogModifier : LightingModifier
{
	private FogMode _mode = FogMode.Simple;
	private Color _color = new(0.5f, 0.6f, 0.7f, 0.3f);
	private Vector2 _range = new(50f, 500f);
	private Color _emission = new(0f, 0f, 0f);
	private float _emissionEnergy = 1f;

	private static readonly bool _supportsVolumetric = RenderingDeviceSwitcher.GetCurrentDriverName() == RenderingDeviceSwitcher.GetRenderingName(RenderingDeviceSwitcher.RenderingDeviceEnum.Forward);

	[Editable, ScriptProperty]
	public FogMode Mode
	{
		get => _mode;
		set
		{
			_mode = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Range
	{
		get => _range;
		set
		{
			_range = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Emission
	{
		get => _emission;
		set
		{
			_emission = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float EmissionEnergy
	{
		get => _emissionEnergy;
		set
		{
			_emissionEnergy = value;
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
		var env = Root.Lighting.environment;
		env.FogEnabled = false;
		env.VolumetricFogEnabled = false;
		base.PreDelete();
	}

	private void ApplyEffects()
	{
		var env = Root.Lighting.environment;
		if (IsHidden) { env.FogEnabled = false; env.VolumetricFogEnabled = false; return; }

		if (_mode == FogMode.Volumetric && _supportsVolumetric)
		{
			env.FogEnabled = false;
			env.VolumetricFogEnabled = true;

			// Beer-Lambert fit over the range window
			float far = Mathf.Max(_range.Y, 0.5f);
			env.VolumetricFogDensity = -Mathf.Log(1f - Mathf.Min(_color.A, 0.999f)) / far;
			env.VolumetricFogLength = _range.Y;

			env.VolumetricFogAlbedo = _color;
			env.VolumetricFogEmission = _emission;
			env.VolumetricFogEmissionEnergy = _emissionEnergy;
		}
		else
		{
			env.VolumetricFogEnabled = false;
			env.FogEnabled = true;
			env.FogLightColor = _color;
			env.FogDensity = _color.A;
			env.FogDepthBegin = _range.X;
			env.FogDepthEnd = _range.Y;
			env.FogSkyAffect = _color.A;
			env.FogSunScatter = _emissionEnergy * _emission.V;
		}
	}

	public override void HiddenChanged(bool to)
	{
		ApplyEffects();
		base.HiddenChanged(to);
	}
}

[ScriptEnum]
public enum FogMode
{
	Simple,
	Volumetric
}
