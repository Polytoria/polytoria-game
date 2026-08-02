// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using System;

#if CREATOR
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Particles : Dynamic
{
	private const double AabbUpdateIntervalSec = 3;
	private GpuParticles3D _particles = null!;
	private ParticleProcessMaterial _particle = null!;

	private double _aabbUpdateTimer = 0;

	private ImageAsset? _asset;
	private TextureFilterEnum _textureFilter;
	private ColorSeries _color = new();
	private NumberRange _lifetime = new() { Min = 0.5f, Max = 1f };
	private int _amount = 8;
	private float _explosiveness = 0;
	private Vector3 _gravity;
	private Vector3 _velocityDirection;
	private NumberRange _initialVelocity;
	private NumberRange _startRotation;

	private NumberRange _angularVelocity;
	private NumberSeries _angularVelocityOverLifetime = new(0f, 0f);

	private NumberRange _linearAcceleration;
	private NumberSeries _linearAccelerationOverLifetime = new(0f, 0f);

	private NumberRange _radialAcceleration;
	private NumberSeries _radialAccelerationOverLifetime = new(0f, 0f);

	private NumberRange _tangentialAcceleration;
	private NumberSeries _tangentialAccelerationOverLifetime = new(0f, 0f);

	private NumberRange _orbitalVelocity;
	private NumberSeries _orbitalVelocityOverLifetime = new(0f, 0f);


	private NumberRange _damping;
	private NumberSeries _dampingOverLifetime = new(0f, 0f);

	private NumberRange _scaleOverVelocity;
	private NumberSeries _scaleOverVelocityCurve = new(1f, 1f);

	private bool _turbulenceEnabled;
	private NumberRange _turbulenceInfluence = new(0.1f, 0.1f);
	private NumberSeries _turbulenceOverLifetime = new(0f, 0f);
	private float _turbulenceNoiseScale = 9f;
	private float _turbulenceNoiseStrength = 1f;
	private Vector3 _turbulenceNoiseSpeed = new();

	private float _spread = 45;
	private float _flatness = 0;
	private NumberRange _scale = new(1f, 1f);
	private NumberSeries _scaleOverLifetime = new(1f, 1f);
	private NumberRange _hueVariation = new(0f, 0f);
	private ParticleSimulationSpaceEnum _simulationSpace;
	private ParticleEmissionShapeEnum _emissionShape;
	private ParticleOrientationEnum _orientation;
	private Vector3 _emissionShapeScale;
	private BlendModeEnum _blendMode;
	private bool _shaded = true;

	private Godot.Mesh _mesh = null!;
	private StandardMaterial3D _material = null!;

	private bool _paused;
	private float _speedScale = 1f;
	private float _previousSpeedScale = 1f;

	[Editable, ScriptProperty]
	public bool Playing
	{
		get => _particles.Emitting;
		set
		{
			if (value) Play();
			else Stop();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float SpeedScale
	{
		get => _speedScale;
		set
		{
			_speedScale = value;
			_particles.SpeedScale = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			_material.AlbedoTexture = null;
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(TextureFilterEnum.Linear)]
	public TextureFilterEnum TextureFilter
	{
		get => _textureFilter;
		set
		{
			_textureFilter = value;
			_material.TextureFilter = value switch
			{
				TextureFilterEnum.Nearest => BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
				TextureFilterEnum.NearestNoMipmaps => BaseMaterial3D.TextureFilterEnum.Nearest,
				TextureFilterEnum.Linear => BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				TextureFilterEnum.LinearNoMipmaps => BaseMaterial3D.TextureFilterEnum.Linear,
				_ => throw new IndexOutOfRangeException("Texture filter mode out of range"),
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ColorSeries Color
	{
		get => _color;
		set
		{
			_color = value;
			_particle.ColorRamp = value.ToGradientTexture1D();
			_particle.AlphaCurve = new CurveTexture() { Curve = value.ToAlphaCurve() };
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange Lifetime
	{
		get => _lifetime;
		set
		{
			_lifetime = value;

			// Lifetime is the average of Min and Max
			_particles.Lifetime = (_lifetime.Min + _lifetime.Max) / 2f;

			// half the total spread
			_particle.LifetimeRandomness = (_lifetime.Max - _lifetime.Min) / 2f;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, ScriptLegacyProperty("MaxParticles")]
	public int Amount
	{
		get => _amount;
		set
		{
			_amount = value;
			_particles.Amount = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Explosiveness
	{
		get => _explosiveness;
		set
		{
			_explosiveness = value;
			_particles.Explosiveness = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 Gravity
	{
		get => _gravity;
		set
		{
			_gravity = value;
			_particle.Gravity = _gravity;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 VelocityDirection
	{
		get => _velocityDirection;
		set
		{
			_velocityDirection = value;

			_particle.Direction = _velocityDirection;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange InitialVelocity
	{
		get => _initialVelocity;
		set
		{
			_initialVelocity = value;

			_particle.InitialVelocityMin = _initialVelocity.Min;
			_particle.InitialVelocityMax = _initialVelocity.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange StartRotation
	{
		get => _startRotation;
		set
		{
			_startRotation = value;

			_particle.AngleMin = _startRotation.Min;
			_particle.AngleMax = _startRotation.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange AngularVelocity
	{
		get => _angularVelocity;
		set
		{
			_angularVelocity = value;

			_particle.AngularVelocityMin = _angularVelocity.Min;
			_particle.AngularVelocityMax = _angularVelocity.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries AngularVelocityOverLifetime
	{
		get => _angularVelocityOverLifetime;
		set
		{
			_angularVelocityOverLifetime = value;
			_particle.AngularVelocityCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange LinearAcceleration
	{
		get => _linearAcceleration;
		set
		{
			_linearAcceleration = value;

			_particle.LinearAccelMin = _linearAcceleration.Min;
			_particle.LinearAccelMax = _linearAcceleration.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries LinearAccelerationOverLifetime
	{
		get => _linearAccelerationOverLifetime;
		set
		{
			_linearAccelerationOverLifetime = value;
			_particle.LinearAccelCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange RadialAcceleration
	{
		get => _radialAcceleration;
		set
		{
			_radialAcceleration = value;

			_particle.RadialAccelMin = _radialAcceleration.Min;
			_particle.RadialAccelMax = _radialAcceleration.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries RadialAccelerationOverLifetime
	{
		get => _radialAccelerationOverLifetime;
		private set
		{
			_radialAccelerationOverLifetime = value;
			_particle.RadialAccelCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange TangentialAcceleration
	{
		get => _tangentialAcceleration;
		set
		{
			_tangentialAcceleration = value;

			_particle.TangentialAccelMin = _tangentialAcceleration.Min;
			_particle.TangentialAccelMax = _tangentialAcceleration.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries TangentialAccelerationOverLifetime
	{
		get => _tangentialAccelerationOverLifetime;
		private set
		{
			_tangentialAccelerationOverLifetime = value;
			_particle.TangentialAccelCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange OrbitalVelocity
	{
		get => _orbitalVelocity;
		set
		{
			_orbitalVelocity = value;

			_particle.OrbitVelocityMin = _orbitalVelocity.Min;
			_particle.OrbitVelocityMax = _orbitalVelocity.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries OrbitalVelocityOverLifetime
	{
		get => _orbitalVelocityOverLifetime;
		private set
		{
			_orbitalVelocityOverLifetime = value;
			_particle.OrbitVelocityCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange Damping
	{
		get => _damping;
		set
		{
			_damping = value;

			_particle.DampingMin = _damping.Min;
			_particle.DampingMax = _damping.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries DampingOverLifetime
	{
		get => _dampingOverLifetime;
		private set
		{
			_dampingOverLifetime = value;
			_particle.DampingCurve = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange ScaleOverVelocity
	{
		get => _scaleOverVelocity;
		set
		{
			_scaleOverVelocity = value;

			_particle.ScaleOverVelocityMin = _scaleOverVelocity.Min;
			_particle.ScaleOverVelocityMax = _scaleOverVelocity.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries ScaleOverVelocityCurve
	{
		get => _scaleOverVelocityCurve;
		private set
		{
			_scaleOverVelocityCurve = value;

			bool allZero = true;
			float[] vals = value.GetValues();
			for (int i = 0; i < vals.Length; i++)
			{
				if (vals[i] != 0f) { allZero = false; break; }
			}
			_particle.ScaleOverVelocityCurve = allZero ? null : value.ToCurveTexture();

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool TurbulenceEnabled
	{
		get => _turbulenceEnabled;
		set
		{
			_turbulenceEnabled = value;
			_particle.TurbulenceEnabled = _turbulenceEnabled;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange TurbulenceInfluence
	{
		get => _turbulenceInfluence;
		set
		{
			_turbulenceInfluence = value;

			_particle.TurbulenceInfluenceMin = _turbulenceInfluence.Min;
			_particle.TurbulenceInfluenceMax = _turbulenceInfluence.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries TurbulenceOverLifetime
	{
		get => _turbulenceOverLifetime;
		private set
		{
			_turbulenceOverLifetime = value;
			_particle.TurbulenceInfluenceOverLife = value.ToCurveTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float TurbulenceNoiseScale
	{
		get => _turbulenceNoiseScale;
		set
		{
			_turbulenceNoiseScale = value;
			_particle.TurbulenceNoiseScale = _turbulenceNoiseScale;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float TurbulenceNoiseStrength
	{
		get => _turbulenceNoiseStrength;
		set
		{
			_turbulenceNoiseStrength = value;
			_particle.TurbulenceNoiseStrength = _turbulenceNoiseStrength;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 TurbulenceNoiseSpeed
	{
		get => _turbulenceNoiseSpeed;
		set
		{
			_turbulenceNoiseSpeed = value;
			_particle.TurbulenceNoiseSpeed = _turbulenceNoiseSpeed;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Spread
	{
		get => _spread;
		set
		{
			_spread = value;

			_particle.Spread = Mathf.Clamp(_spread, 0, 180);

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Flatness
	{
		get => _flatness;
		set
		{
			_flatness = value;

			_particle.Flatness = Mathf.Clamp(_flatness, 0, 1);

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange Scale
	{
		get => _scale;
		set
		{
			_scale = value;

			_particle.ScaleMin = _scale.Min;
			_particle.ScaleMax = _scale.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberSeries ScaleOverLifetime
	{
		get => _scaleOverLifetime;
		set
		{
			_scaleOverLifetime = value;

			bool allZero = true;
			float[] vals = value.GetValues();
			for (int i = 0; i < vals.Length; i++)
			{
				if (vals[i] != 0f) { allZero = false; break; }
			}
			_particle.ScaleCurve = allZero ? null : value.ToCurveTexture();

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange HueVariation
	{
		get => _hueVariation;
		set
		{
			_hueVariation = value;

			_particle.HueVariationMin = _hueVariation.Min;
			_particle.HueVariationMax = _hueVariation.Max;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public BlendModeEnum BlendMode
	{
		get => _blendMode;
		set
		{
			_blendMode = value;
			_material.BlendMode = _blendMode switch
			{
				BlendModeEnum.Mix => StandardMaterial3D.BlendModeEnum.Mix,
				BlendModeEnum.Add => StandardMaterial3D.BlendModeEnum.Add,
				BlendModeEnum.Subtract => StandardMaterial3D.BlendModeEnum.Sub,
				BlendModeEnum.Multiply => StandardMaterial3D.BlendModeEnum.Mul,
				_ => StandardMaterial3D.BlendModeEnum.Mix,
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Shaded
	{
		get => _shaded;
		set
		{
			_shaded = value;
			_material.ShadingMode = _shaded ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ParticleEmissionShapeEnum EmissionShape
	{
		get => _emissionShape;
		set
		{
			_emissionShape = value;

			_particle.EmissionShape = _emissionShape switch
			{
				ParticleEmissionShapeEnum.Sphere => ParticleProcessMaterial.EmissionShapeEnum.Sphere,
				ParticleEmissionShapeEnum.SphereSurface => ParticleProcessMaterial.EmissionShapeEnum.SphereSurface,
				ParticleEmissionShapeEnum.Box => ParticleProcessMaterial.EmissionShapeEnum.Box,
				ParticleEmissionShapeEnum.Ring => ParticleProcessMaterial.EmissionShapeEnum.Ring,
				_ => ParticleProcessMaterial.EmissionShapeEnum.Point,
			};

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 EmissionShapeScale
	{
		get => _emissionShapeScale;
		set
		{
			_emissionShapeScale = value;
			_particle.EmissionShapeScale = _emissionShapeScale;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ParticleSimulationSpaceEnum SimulationSpace
	{
		get => _simulationSpace;
		set
		{
			_simulationSpace = value;
			_particles.LocalCoords = _simulationSpace == ParticleSimulationSpaceEnum.Local;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ParticleOrientationEnum Orientation
	{
		get => _orientation;
		set
		{
			_orientation = value;

			_material.BillboardMode = _orientation switch
			{
				ParticleOrientationEnum.FaceCamera => BaseMaterial3D.BillboardModeEnum.Particles,
				ParticleOrientationEnum.FaceCameraFixedY => BaseMaterial3D.BillboardModeEnum.FixedY,
				_ => BaseMaterial3D.BillboardModeEnum.Disabled,
			};

			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public bool IsPlaying => _particles.Emitting && !_paused;

	[ScriptProperty]
	public bool IsPaused => _paused;

	[ScriptProperty]
	public bool IsStopped => !_particles.Emitting && !_paused;

	private void OnResourceLoaded(Resource tex)
	{
		_material.AlbedoTexture = (Texture2D)tex;
	}

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(new SpatialIcon(ClassName), @internal: Node.InternalMode.Back);
#endif
		base.Init();
		_particle = new();
		GDNode3D.AddChild(_particles = new() { ProcessMaterial = _particle }, false, Node.InternalMode.Front);
		_material = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
		_mesh = new QuadMesh() { Material = _material };

		_material.BillboardKeepScale = true;
		_material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;

		_particles.DrawPass1 = _mesh;

		// Workaround for triggering particles to play even if they're not visible
		_particles.VisibilityAabb = new Aabb().Grow(1000000);
		SetProcess(true);
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_particles.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	public override void InitOverrides()
	{
		Shaded = true;
		Image = null;
		Amount = 8;
		Color = new();
		Spread = 45;
		Flatness = 0;
		VelocityDirection = new(1, 0, 0);
		InitialVelocity = new() { Min = 0f, Max = 0f };
		Gravity = new(0, -9.8f, 0);
		Lifetime = new() { Min = 1f, Max = 1f };
		Scale = new() { Min = 1f, Max = 1f };
		HueVariation = new() { Min = 0f, Max = 0f };
		SimulationSpace = ParticleSimulationSpaceEnum.Local;
		EmissionShape = ParticleEmissionShapeEnum.Point;
		EmissionShapeScale = Vector3.One;
		BlendMode = BlendModeEnum.Mix;
		Orientation = ParticleOrientationEnum.FaceCamera;
		ScaleOverLifetime = new(1f, 1f);
		ScaleOverVelocityCurve = new(1f, 1f);
		base.InitOverrides();
	}

	public override void Process(double delta)
	{
		_aabbUpdateTimer += delta;
		if (_aabbUpdateTimer >= AabbUpdateIntervalSec)
		{
			_aabbUpdateTimer = 0;
			UpdateVisibilityAabb();
		}
		base.Process(delta);
	}

	private void UpdateVisibilityAabb()
	{
		Aabb captured = _particles.CaptureAabb();
		if (captured.Size != Vector3.Zero)
			// Double the size
			_particles.VisibilityAabb = new Aabb(captured.Position - captured.Size * 0.5f, captured.Size * 2f);
	}

	[ScriptMethod]
	public void Play()
	{
		_paused = false;
		SpeedScale = _previousSpeedScale <= 0 ? 1f : _previousSpeedScale;
		_particles.Emitting = true;
		OnPropertyChanged(nameof(Playing));
	}

	[ScriptMethod]
	public void Pause()
	{
		if (_paused) return;

		_previousSpeedScale = _speedScale;
		SpeedScale = 0f;
		_paused = true;
	}

	[ScriptMethod]
	public void Stop()
	{
		_paused = false;
		SpeedScale = _previousSpeedScale <= 0 ? 1f : _previousSpeedScale;
		_particles.Emitting = false;
		OnPropertyChanged(nameof(Playing));
	}

	[ScriptMethod]
	public void Clear()
	{
		bool wasEmitting = _particles.Emitting;
		_particles.Restart();
		_particles.Emitting = wasEmitting && !_paused;
	}

	[ScriptMethod]
	public void Emit(int count)
	{
		Rpc(nameof(NetEmit), count);
	}

	[NetRpc(Networking.AuthorityMode.Authority, CallLocal = true, TransferMode = Networking.TransferMode.Reliable)]
	private void NetEmit(int count)
	{
		GpuParticles3D temp = (GpuParticles3D)_particles.Duplicate();
		GDNode.AddChild(temp, @internal: Node.InternalMode.Back);
		temp.Amount = count;
		temp.Explosiveness = 1;
		temp.OneShot = true;
		temp.Emitting = true;

		void finishEmit()
		{
			temp.Finished -= finishEmit;
			temp.QueueFree();
		}

		temp.Finished += finishEmit;
	}

	[ScriptEnum]
	public enum ParticleSimulationSpaceEnum
	{
		Local,
		World
	}

	[ScriptEnum]
	public enum ParticleEmissionShapeEnum
	{
		Point,
		Sphere,
		SphereSurface,
		Box,
		Ring
	}

	[ScriptEnum]
	public enum ParticleOrientationEnum
	{
		FaceCamera,
		FaceCameraFixedY
	}
}
