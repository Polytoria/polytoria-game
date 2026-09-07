// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using Polytoria.Datamodel.Resources;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Static]
public sealed partial class Sky : Instance
{
	private ShaderMaterial _mat = null!;
	private bool _applyingPreset;
	private SkyboxAsset? _skybox;
	private float _transitionDuration = 1f;
	private readonly Texture2D _empty = GD.Load<Texture2D>("res://assets/textures/empty.png");

	private Color _liveTop, _liveBottom, _liveHorizonColor;
	private float _liveGradExp, _liveHorizonExp, _liveHorizonContrib;
	private GradientSkyboxAsset? _transitionTarget;
	private float _transitionElapsed;
	private bool _transitioning;

	[Editable, ScriptProperty]
	public float TransitionDuration
	{
		get => _transitionDuration;
		set
		{
			_transitionDuration = Mathf.Max(value, 0f);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public SkyboxAsset? Skybox
	{
		get => _skybox;
		set
		{
			SkyboxAsset? previous = _skybox;
			_skybox?.Changed -= OnSkyboxChanged;
			_skybox = value;
			_skybox?.Changed += OnSkyboxChanged;

			BeginTransition(previous, value);
			MarkCustom();
			OnPropertyChanged();
		}
	}

	private void BeginTransition(SkyboxAsset? from, SkyboxAsset? to)
	{
		if (from is GradientSkyboxAsset && to is GradientSkyboxAsset toGrad && _transitionDuration > 0f)
		{
			_transitionTarget = toGrad;
			_transitionElapsed = 0f;
			_transitioning = true;
		}
		else
		{
			_transitioning = false;
			if (to is GradientSkyboxAsset instantGrad) CaptureLiveGradient(instantGrad);
			Refresh();
		}
	}

	private void CaptureLiveGradient(GradientSkyboxAsset g)
	{
		_liveTop = g.SkyGradientTop;
		_liveBottom = g.SkyGradientBottom;
		_liveGradExp = g.SkyGradientExponent;
		_liveHorizonColor = g.HorizonLineColor;
		_liveHorizonExp = g.HorizonLineExponent;
		_liveHorizonContrib = g.HorizonLineContribution;
	}

	public override void Process(double delta)
	{
		if (_transitioning && _transitionTarget != null)
		{
			_transitionElapsed += (float)delta;
			float t = Mathf.Clamp(_transitionElapsed / _transitionDuration, 0f, 1f);

			_liveTop = _liveTop.Lerp(_transitionTarget.SkyGradientTop, t);
			_liveBottom = _liveBottom.Lerp(_transitionTarget.SkyGradientBottom, t);
			_liveGradExp = Mathf.Lerp(_liveGradExp, _transitionTarget.SkyGradientExponent, t);
			_liveHorizonColor = _liveHorizonColor.Lerp(_transitionTarget.HorizonLineColor, t);
			_liveHorizonExp = Mathf.Lerp(_liveHorizonExp, _transitionTarget.HorizonLineExponent, t);
			_liveHorizonContrib = Mathf.Lerp(_liveHorizonContrib, _transitionTarget.HorizonLineContribution, t);

			PushLiveGradient();
			RefreshNonAsset();

			if (t >= 1f)
			{
				_transitioning = false;
			}
		}
		base.Process(delta);
	}

	private void PushLiveGradient()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("sky_gradient_top", _liveTop);
		_mat.SetShaderParameter("sky_gradient_bottom", _liveBottom);
		_mat.SetShaderParameter("sky_gradient_exponent", _liveGradExp);
		_mat.SetShaderParameter("horizon_line_color", _liveHorizonColor);
		_mat.SetShaderParameter("horizon_line_exponent", _liveHorizonExp);
		_mat.SetShaderParameter("horizon_line_contribution", _liveHorizonContrib);
	}

	private void OnSkyboxChanged()
	{
		MarkCustom();
		Refresh();
	}

	public void ApplyPreset(LightingPreset preset)
	{
		_applyingPreset = true;
		LightingPresets.Apply(this, preset);
		_applyingPreset = false;
		Root.Lighting.ReevaluatePreset();
	}

	private void MarkCustom()
	{
		if (_applyingPreset || !IsPropReady || !Root.IsLoaded) return;
		Root.Lighting.ReevaluatePreset();
	}

	private void Push(string uniform, Variant value)
	{
		_mat.SetShaderParameter(uniform, value);
		MarkCustom();
		OnPropertyChanged();
	}

	// Called by Lighting whenever the clock, a body, or a body's disc properties change
	internal void Refresh()
	{
		if (_mat == null) return;

		// Reset face flags before delegating, otherwise swapping from a CubemapSkyboxAsset
		// back to a GradientSkyboxAsset would leave the previous asset's has_image uniforms stuck on
		if (!_transitioning)
		{
			foreach (string face in FaceNames)
			{
				_mat.SetShaderParameter($"{face}_has_image", false);
			}
			_skybox?.Apply(_mat);
		}
		RefreshNonAsset();
	}

	private void RefreshNonAsset()
	{
		if (_mat == null) return;

		SunLight? sun = Root.Lighting.FindChild<SunLight>("SunLight");
		if (sun != null)
		{
			_mat.SetShaderParameter("sun_disc_color", sun.Color);
			_mat.SetShaderParameter("sun_disc_size", Mathf.Max(sun.Size.Length(), 0.001f));
			_mat.SetShaderParameter("sun_has_image", sun.Image != null);
			if (sun.Image != null) _mat.SetShaderParameter("sun_image", (Texture2D?)sun.Image.Resource ?? _empty);
			_mat.SetShaderParameter("sun_halo_color", sun.HaloColor);
			_mat.SetShaderParameter("sun_halo_exponent", sun.HaloExponent);
			_mat.SetShaderParameter("sun_halo_contribution", sun.HaloContribution);
		}

		MoonLight? moon = Root.Lighting.FindChild<MoonLight>("MoonLight");
		if (moon != null)
		{
			_mat.SetShaderParameter("moon_disc_color", moon.Color);
			_mat.SetShaderParameter("moon_disc_size", Mathf.Max(moon.Size.Length(), 0.001f));
			_mat.SetShaderParameter("moon_has_image", moon.Image != null);
			if (moon.Image != null) _mat.SetShaderParameter("moon_image", (Texture2D?)moon.Image.Resource ?? _empty);
			_mat.SetShaderParameter("moon_halo_color", moon.HaloColor);
			_mat.SetShaderParameter("moon_halo_exponent", moon.HaloExponent);
			_mat.SetShaderParameter("moon_halo_contribution", moon.HaloContribution);
		}

		Stars stars = Root.Lighting.Stars;
		_mat.SetShaderParameter("stars_enabled", stars.Enabled);
		_mat.SetShaderParameter("stars_density", stars.Density);
		_mat.SetShaderParameter("stars_brightness", stars.Brightness);
		_mat.SetShaderParameter("stars_twinkle_speed", stars.TwinkleSpeed);
		_mat.SetShaderParameter("stars_horizon_padding", stars.HorizonPadding);
		_mat.SetShaderParameter("stars_rotation", Basis.FromEuler(stars.Rotation * (Mathf.Pi / 180f)));

		Clouds clouds = Root.Lighting.Clouds;
		_mat.SetShaderParameter("clouds_enabled", clouds.Enabled);
		_mat.SetShaderParameter("clouds_coverage", clouds.Coverage);
		_mat.SetShaderParameter("clouds_softness", clouds.Softness);
		_mat.SetShaderParameter("clouds_scale", clouds.Scale);
		_mat.SetShaderParameter("clouds_speed", clouds.Speed);
		_mat.SetShaderParameter("clouds_color", clouds.Color);

		var bodies = new List<CelestialBody>();
		foreach (var b in Root.Lighting.Bodies)
		{
			if (!b.CastsLight) bodies.Add(b); // Decorative-only bodies
		}

		var dirs = new Vector3[16];
		var colors = new Color[16];
		var sizes = new float[16];
		int count = Math.Min(bodies.Count, 16);
		for (int i = 0; i < count; i++)
		{
			dirs[i] = -((Node3D)bodies[i].GDNode).GlobalTransform.Basis.Z;
			colors[i] = bodies[i].Color;
			sizes[i] = Mathf.Max(bodies[i].Size.Length(), 0.001f);
		}

		_mat.SetShaderParameter("fake_body_dir", dirs);
		_mat.SetShaderParameter("fake_body_color", colors);
		_mat.SetShaderParameter("fake_body_size", sizes);
		_mat.SetShaderParameter("fake_body_count", count);
	}

	private static readonly string[] FaceNames = ["top", "bottom", "left", "right", "front", "back"];

	private void RebuildMaterial()
	{
		_mat = new() { Shader = GD.Load<Shader>("res://resources/shaders/skybox.gdshader") };
		SkyMaterial = _mat;
	}

	public Material SkyMaterial { get; set; } = null!;

	public override void InitOverrides()
	{
		ApplyPreset(LightingPreset.Sunset1);
		base.InitOverrides();
	}

	public override Node CreateGDNode() => new();

	public override void EnterTree()
	{
		base.EnterTree();
		if (_mat != null) Root.Lighting.ApplySky(this);
	}

	public override void Init()
	{
		RebuildMaterial();
		base.Init();
		Root.Lighting.ApplySky(this);
		SetProcess(true);
	}

	public override void ExitTree()
	{
		base.ExitTree();
		Root.Lighting.RemoveSky(this);
	}
}