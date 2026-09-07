// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
#if CREATOR
using Polytoria.Creator.Settings;
#endif

namespace Polytoria.Datamodel;

[Instantiable]
public partial class SunRaysModifier : LightingModifier
{
	private bool _enabled = true;
	private float _intensity = 0.5f;
	private float _length = 0.5f;
	private Color _tint = new(1.0f, 0.95f, 0.8f);

	private CanvasLayer _canvasLayer = null!;
	private ColorRect _rect = null!;
	private ShaderMaterial _mat = null!;

	private static readonly Shader _shader = GD.Load<Shader>("res://resources/shaders/sunrays.gdshader");

	private const float ReferenceFovDegrees = 75f;
	private const float SunRayDistance = 5000f;
	private const float OcclusionSmoothing = 8f;
	private float _sunOcclusion = 1f;

	[Editable, ScriptProperty, DefaultValue(true)]
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

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Intensity
	{
		get => _intensity;
		set
		{
			_intensity = Mathf.Clamp(value, 0f, 2f);
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.5f)]
	public float Length
	{
		get => _length;
		set
		{
			_length = Mathf.Clamp(value, 0f, 1f);
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Tint
	{
		get => _tint;
		set
		{
			_tint = value;
			ApplyEffects();
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode() => new Node();

	public override void Init()
	{
		base.Init();

		_mat = new ShaderMaterial { Shader = _shader };

		_canvasLayer = new CanvasLayer { Layer = 10 };
		(Root.RootViewport ?? (Node)Root.GDNode).AddChild(_canvasLayer);

		_rect = new ColorRect { AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = Control.MouseFilterEnum.Ignore, Material = _mat };
		_canvasLayer.AddChild(_rect);

		ApplyEffects();
		SetProcess(true);
	}

	public override void PreDelete()
	{
		if (Node.IsInstanceValid(_canvasLayer)) _canvasLayer.QueueFree();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		UpdateSunScreenPos(delta);
		base.Process(delta);
	}

	private void UpdateSunScreenPos(double delta)
	{
		_rect.Visible = false;
		if (IsHidden || !_enabled) return;

		Camera3D? cam = Root.Environment.CurrentGDCamera;
		if (cam == null) return;

		SunLight? sun = Root.Lighting.Sun;
		if (sun == null) return;

		Vector3 towardSun = -((Node3D)sun.GDNode).GlobalTransform.Basis.Z.Normalized();
		Vector3 camForward = -cam.GlobalTransform.Basis.Z.Normalized();
		if (camForward.Dot(towardSun) <= 0f) return;

		Vector2 viewportSize = cam.GetViewport().GetVisibleRect().Size;
		if (viewportSize == Vector2.Zero) return;
		Vector2 screenPos = cam.UnprojectPosition(cam.GlobalPosition + towardSun * 1000f);
		Vector2 screenUV = screenPos / viewportSize;

		_rect.Visible = true;
		_mat.SetShaderParameter("light_pos_screen", screenUV);
		_mat.SetShaderParameter("light_dir", -towardSun);
		_mat.SetShaderParameter("camera_dir", camForward);

		// Keeps ray reach tied to a fixed angular width
		float fovCompensation = cam.Projection == Camera3D.ProjectionType.Perspective ? Mathf.Tan(Mathf.DegToRad(ReferenceFovDegrees * 0.5f)) / Mathf.Tan(Mathf.DegToRad(cam.Fov * 0.5f)) : 1f;
		_mat.SetShaderParameter("fov_compensation", fovCompensation);

		// Physics raycast toward the sun, so occlusion is non-renderer-dependent
		Vector3 origin = cam.GlobalPosition;
		var query = PhysicsRayQueryParameters3D.Create(origin, origin + towardSun * SunRayDistance);
		query.CollideWithAreas = false;
		bool occluded = cam.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;

		// Interpolate occluded transition
		float targetOcclusion = occluded ? 0f : 1f;
		_sunOcclusion = Mathf.Lerp(_sunOcclusion, targetOcclusion, 1f - Mathf.Exp(-OcclusionSmoothing * (float)delta));
		_mat.SetShaderParameter("sun_occlusion", _sunOcclusion);
	}

	private void ApplyEffects()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("enabled", _enabled && !IsHidden);
		_mat.SetShaderParameter("intensity", _intensity);
		_mat.SetShaderParameter("ray_length", _length);
		_mat.SetShaderParameter("tint", new Vector3(_tint.R, _tint.G, _tint.B));
		_mat.SetShaderParameter("sample_count", GetSampleCount());
	}

	private static int GetSampleCount()
	{
		if (RenderingDeviceSwitcher.GetCurrentDriverName() == RenderingDeviceSwitcher.GetRenderingName(RenderingDeviceSwitcher.RenderingDeviceEnum.GLCompatibility))
		{
			return 64;
		}

		ISettingsContext? settings =
#if CREATOR
		(ISettingsContext?)CreatorSettingsService.Instance ??
#endif
		ClientSettingsService.Instance;

		GraphicsPreset preset = settings?.Get<GraphicsPreset>(SharedSettingKeys.Graphics.Preset) ?? GraphicsPreset.Medium;
		return preset switch
		{
			GraphicsPreset.Low => 64,
			GraphicsPreset.High or GraphicsPreset.Ultra or GraphicsPreset.Photo => 128,
			_ => 96
		};
	}

	public override void HiddenChanged(bool to)
	{
		ApplyEffects();
		base.HiddenChanged(to);
	}
}
