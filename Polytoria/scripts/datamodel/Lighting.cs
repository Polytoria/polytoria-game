// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Datamodel.Data;
using Polytoria.Shared.Settings;
using System.Collections.Generic;

#if CREATOR
using Polytoria.Creator;
using Polytoria.Creator.Settings;
#endif
using Polytoria.Shared;
using ObsoleteAttribute = Polytoria.Attributes.ObsoleteAttribute;

namespace Polytoria.Datamodel;

[Static("Lighting")]
public sealed partial class Lighting : Instance
{
	private WorldEnvironment _worldEnv = null!;
	internal Godot.Environment environment = null!;
	private Godot.Sky _sky = null!;
	internal CameraAttributesPractical cameraAttributes = null!;

	private readonly List<CelestialBody> _bodies = new();
	private bool _syncingBody;
	private float _clockTime = 12f;
	private float _latitude = 41f;
	private bool _followsClock = true;
	private readonly Dictionary<CelestialBody, Action<string>> _bodyCallbacks = [];

	public bool CustomSkyApplied { get; private set; }
	private Sky? _currentSky;
	private LightingPreset _currentPreset = LightingPreset.Custom;

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		_worldEnv = (WorldEnvironment)GDNode;
		environment = _worldEnv.Environment;
		_sky = environment.Sky;
		environment.BackgroundMode = Godot.Environment.BGMode.Sky;

		cameraAttributes = new();
		_worldEnv.CameraAttributes = cameraAttributes;

		// Compatibility can't sample custom sky shaders for ambient (would produce pitch black)
		if (RenderingDeviceSwitcher.GetCurrentDriverName() == RenderingDeviceSwitcher.GetRenderingName(RenderingDeviceSwitcher.RenderingDeviceEnum.GLCompatibility))
		{
			environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
			environment.AmbientLightColor = new Color(0.3f, 0.3f, 0.35f);
			environment.AmbientLightEnergy = 0.8f;
		}

#if CREATOR
		if (CreatorSettingsService.Instance != null)
		{
			ApplyGraphicsSettings(CreatorSettingsService.Instance);
		}
		else
#endif
		{
			ApplyGraphicsSettings(ClientSettingsService.Instance);
		}

		if (Root.IsLoaded)
		{
			UpdateCelestialPositions();
		}
		else
		{
			Root.Loaded.Once(UpdateCelestialPositions);
		}

		base.Init();
	}

	public override void PreDelete()
	{
		base.PreDelete();
	}

	public void ApplyGraphicsSettings(ISettingsContext settings)
	{
		bool mobile = Globals.IsMobileBuild;

		bool glow = settings.Get<bool>(SharedSettingKeys.PostProcessing.Glow);
		bool ssao = settings.Get<bool>(SharedSettingKeys.PostProcessing.Ssao);
		bool ssr = settings.Get<bool>(SharedSettingKeys.PostProcessing.Ssr);
		bool ssil = settings.Get<bool>(SharedSettingKeys.PostProcessing.Ssil);
		bool sdfgi = settings.Get<bool>(SharedSettingKeys.PostProcessing.Sdfgi);

		if (mobile)
		{
			glow = false;
			ssao = false;
			ssr = false;
			ssil = false;
			sdfgi = false;
		}

		environment.GlowEnabled = glow;
		environment.SsaoEnabled = ssao;
		environment.SsrEnabled = ssr;
		environment.SsilEnabled = ssil;
		environment.SdfgiEnabled = sdfgi;

		environment.SdfgiCascades = settings.Get<int>(SharedSettingKeys.PostProcessing.SdfgiCascades);
		environment.SdfgiMinCellSize = settings.Get<float>(SharedSettingKeys.PostProcessing.SdfgiCellSize);
		environment.SsilRadius = settings.Get<float>(SharedSettingKeys.PostProcessing.SsilRadius);
	}

	public void ApplySky(Sky sky)
	{
		if (sky.IsHidden) return;
		CustomSkyApplied = true;
		_sky.SkyMaterial = sky.SkyMaterial;
		_currentSky = sky;
		sky.Refresh();
		if (Root.IsLoaded) ReevaluatePreset();
	}

	public void RemoveSky(Sky sky)
	{
		if (_currentSky != sky) { return; }
		CustomSkyApplied = false;
	}

	public Sky? CurrentSky => _currentSky;
	public IReadOnlyList<CelestialBody> Bodies => _bodies;

	public void UpdateSky()
	{
		if (_currentSky == null || !Root.IsLoaded) return;
		_currentSky?.Refresh();
		ReevaluatePreset();
	}

	public SunLight Sun => FindChild<SunLight>("SunLight")!;
	public MoonLight Moon => FindChild<MoonLight>("MoonLight")!;
	public Stars Stars => FindChild<Stars>("Stars")!;
	public Clouds Clouds => FindChild<Clouds>("Clouds")!;

	internal void RegisterBody(CelestialBody body)
	{
		_bodies.Add(body);
		Action<string> callback = name => OnBodyPropertyChanged(body, name);
		_bodyCallbacks[body] = callback;
		body.PropertyChanged.Connect(callback);
		SyncBodyToClock(body);
	}

	internal void UnregisterBody(CelestialBody body)
	{
		_bodies.Remove(body);
		if (_bodyCallbacks.TryGetValue(body, out var callback))
		{
			body.PropertyChanged.Disconnect(callback);
			_bodyCallbacks.Remove(body);
		}
		UpdateSky();
	}

	private void OnBodyPropertyChanged(CelestialBody body, string propertyName)
	{
		if (propertyName != nameof(CelestialBody.Rotation) || _syncingBody || !body.IsPropReady) return;
		if (!_followsClock) return;
		float orbitOffset = body is SunLight ? 0f : 180f;
		Vector3 expectedRot = ComputeArcRotation(_clockTime, _latitude, orbitOffset);
		if (body.Rotation.IsEqualApprox(expectedRot)) return;
		SyncClockFromBody(body);
	}

	internal bool IsSyncingBody(CelestialBody body) => _syncingBody;

	internal void SyncBodyToClock(CelestialBody body)
	{
		if (!_followsClock || !(body is SunLight || body is MoonLight)) return;
		float orbitOffset = body is SunLight ? 0f : 180f;
		_syncingBody = true;
		Vector3 rot = ComputeArcRotation(_clockTime, _latitude, orbitOffset);
		body.Rotation = rot;
		_syncingBody = false;
	}

	internal void SyncClockFromBody(CelestialBody body)
	{
		if (!(body is SunLight || body is MoonLight)) return;
		float orbitOffset = body is SunLight ? 0f : 180f;
		_followsClock = false;
		_clockTime = ComputeClockFromRotation(body.Rotation, orbitOffset);
		foreach (var other in _bodies)
		{
			if (other != body) SyncBodyToClock(other);
		}
		UpdateSky();
	}

	private void UpdateCelestialPositions()
	{
		foreach (var body in _bodies)
		{
			SyncBodyToClock(body);
		}
		UpdateSky();
	}

	// Builds a world "toward the sun" direction from clock time + latitude, then derives
	// the Euler rotation whose light points along it
	private static Vector3 ComputeArcRotation(float clockTime, float latitude, float orbitOffsetDeg)
	{
		float maxElevationDeg = Mathf.Clamp(90f - Mathf.Abs(latitude), 10f, 90f);

		float phase = (clockTime / 24f) + (orbitOffsetDeg / 360f);
		float phaseAngle = phase * Mathf.Tau;

		float elevationRad = Mathf.DegToRad(maxElevationDeg) * Mathf.Cos(phaseAngle);
		float azimuthRad = phaseAngle;

		Vector3 towardSun = new(Mathf.Sin(azimuthRad) * Mathf.Cos(elevationRad), Mathf.Sin(elevationRad), Mathf.Cos(azimuthRad) * Mathf.Cos(elevationRad));
		Vector3 lightTravelDirection = -towardSun;
		Vector3 upHint = Mathf.Abs(lightTravelDirection.Y) > 0.99f ? Vector3.Back : Vector3.Up;
		Basis basis = Basis.LookingAt(lightTravelDirection, upHint);
		return basis.GetEuler() * (180f / Mathf.Pi);
	}

	private static float ComputeClockFromRotation(Vector3 rotationDegrees, float orbitOffsetDeg)
	{
		Vector3 rotationRad = rotationDegrees * (Mathf.Pi / 180f);
		Basis basis = Basis.FromEuler(rotationRad);
		Vector3 towardSun = basis.Z;
		float azimuthRad = Mathf.Atan2(towardSun.X, towardSun.Z);
		float clockTime = (azimuthRad - Mathf.DegToRad(orbitOffsetDeg)) / Mathf.Tau * 24f;
		return ((clockTime % 24f) + 24f) % 24f;
	}

	// Highest elevation light-casting body
	public CelestialBody? PrimaryLightBody
	{
		get
		{
			CelestialBody? best = null;
			float bestElevation = float.NegativeInfinity;
			foreach (var b in _bodies)
			{
				if (!b.CastsLight) continue;
				float elevation = Mathf.Sin(Mathf.DegToRad(b.Rotation.X));
				if (elevation > bestElevation)
				{
					bestElevation = elevation;
					best = b;
				}
			}
			return best;
		}
	}

	private AmbientSourceEnum _ambientSource;
	private Color _ambientColor;

	[Editable, ScriptProperty]
	public LightingPreset Preset
	{
		get => _currentPreset;
		set
		{
			_currentPreset = value;
			if (value != LightingPreset.Custom) _currentSky?.ApplyPreset(value);
			OnPropertyChanged();
		}
	}

	internal void ReevaluatePreset()
	{
		if (_currentSky == null) return;
		_currentPreset = LightingPresets.FindMatchingPreset(_currentSky) ?? LightingPreset.Custom;
		OnPropertyChanged(nameof(Preset));
	}

	[Editable, ScriptProperty]
	public float ClockTime
	{
		get => _clockTime;
		set
		{
			_clockTime = ((value % 24f) + 24f) % 24f;
			UpdateCelestialPositions();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Latitude
	{
		get => _latitude;
		set
		{
			_latitude = value;
			UpdateCelestialPositions();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool FollowsClock
	{
		get => _followsClock;
		set
		{
			_followsClock = value;
			if (value) UpdateCelestialPositions();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public AmbientSourceEnum AmbientSource
	{
		get => _ambientSource;
		set
		{
			_ambientSource = value;
			environment.AmbientLightSource = value == AmbientSourceEnum.Skybox
				? Godot.Environment.AmbientSource.Bg
				: Godot.Environment.AmbientSource.Color;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color AmbientColor
	{
		get => _ambientColor;
		set
		{
			_ambientColor = value;
			environment.AmbientLightColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Exposure
	{
		get => environment.TonemapExposure;
		set
		{
			environment.TonemapExposure = value;
			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public string GetTimeOfDay()
	{
		int h = (int)_clockTime, m = (int)((_clockTime - h) * 60);
		return $"{h:D2}:{m:D2}:00";
	}

	[ScriptMethod]
	public void SetTimeOfDay(string time)
	{
		var parts = time.Split(':');
		ClockTime = int.Parse(parts[0]) + int.Parse(parts[1]) / 60f;
	}

	[ScriptEnum]
	public enum AmbientSourceEnum
	{
		Skybox,
		Color
	}
}
