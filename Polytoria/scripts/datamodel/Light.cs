// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;
using Polytoria.Datamodel.Resources;
#if CREATOR
using Polytoria.Creator.Settings;
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Abstract]
public partial class Light : Dynamic
{
	const float IntensityConversion = 4f;
	const float ShadowFadeDistance = 100f;
	const float FadeDistance = 250f;
	internal Light3D GDLight = null!;
	private bool _enabled = true;
	private Color _color = new(1, 1, 1);
	private float _brightness = 2;
	private float _lightSize = 0;
	private float _specular = 0.5f;
	private bool _shadows = false;
	private ImageAsset? _projector;
	private Texture2D? _prevImg;

	private static Action? ShadowSettingsChanged;
	private static readonly HashSet<Light> _shadowCasters = [];
	private static readonly List<Light> _shadowRanking = [];
	internal static bool ShadowBudgetDirty;
	private bool _hasShadowBudget = true;
	private float _shadowScore;

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		GDLight.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	[Editable, ScriptProperty]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			GDLight.Visible = value;
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
			GDLight.LightColor = value;
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
			GDLight.LightEnergy = value / IntensityConversion;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float LightSize
	{
		get => _lightSize;
		set
		{
			_lightSize = value;
			GDLight.LightSize = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Specular
	{
		get => _specular;
		set
		{
			_specular = value;
			GDLight.LightSpecular = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Shadows
	{
		get => _shadows;
		set
		{
			_shadows = value;
			UpdateShadows();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? Projector
	{
		get => _projector;
		set
		{
			if (_projector != null && _projector != value)
			{
				_projector.ResourceLoaded -= OnResourceLoaded;
				_projector.UnlinkFrom(this);
			}
			_projector = value;
			OnResourceLoaded(null);
			if (_projector != null)
			{
				_projector.LinkTo(this);
				_projector.ResourceLoaded += OnResourceLoaded;
				if (_projector.IsResourceLoaded && _projector.Resource != null)
				{
					OnResourceLoaded(_projector.Resource);
				}
				else
				{
					_projector.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	internal void UpdateShadows()
	{
		ShadowQuality quality = GetShadowQuality();
		bool shadows = Shadows && quality != ShadowQuality.Off;

		if (GDLight is DirectionalLight3D sun)
		{
#if !GODOT_MOBILE
			sun.DirectionalShadowMode = quality is ShadowQuality.Low or ShadowQuality.Medium
				? DirectionalLight3D.ShadowMode.Parallel2Splits
				: DirectionalLight3D.ShadowMode.Parallel4Splits;
#endif
		}
		else
		{
			if (shadows)
			{
				if (_shadowCasters.Add(this) && Root?.SessionType == World.SessionTypeEnum.Client)
				{
					_hasShadowBudget = false;
					ShadowBudgetDirty = true;
				}
			}
			else
			{
				_shadowCasters.Remove(this);
			}
			shadows &= _hasShadowBudget;
		}

		GDLight.ShadowEnabled = shadows;
		UpdateFade();
	}

	protected void UpdateFade()
	{
		if (GDLight is DirectionalLight3D) return;

		GDLight.DistanceFadeBegin = Mathf.Max(FadeDistance, GetRange());
		float shadowFade = GetShadowQuality() switch
		{
			ShadowQuality.Low => 25f,
			ShadowQuality.Medium => 40f,
			ShadowQuality.High => 70f,
			_ => ShadowFadeDistance,
		};
		GDLight.DistanceFadeShadow = Mathf.Min(shadowFade, GDLight.DistanceFadeBegin);
	}

	internal static bool UpdateShadowBudget(Vector3 cameraPosition)
	{
		ShadowBudgetDirty = false;
		if (_shadowCasters.Count == 0) return false;

		int budget = GetShadowQuality() switch
		{
			ShadowQuality.Low => 4,
			ShadowQuality.Medium => 8,
			ShadowQuality.High or ShadowQuality.Ultra => 16,
			_ => 0,
		};

		_shadowRanking.Clear();
		foreach (Light light in _shadowCasters)
		{
			if (!light.GDLight.IsVisibleInTree())
			{
				if (light._hasShadowBudget) light.SetShadowBudget(false);
				continue;
			}

			float range = light.GetRange();
			light._shadowScore = light.GDLight.GlobalPosition.DistanceTo(cameraPosition) - range;
			if (light._hasShadowBudget)
			{
				light._shadowScore -= Mathf.Max(8f, range * 0.25f);
			}
			_shadowRanking.Add(light);
		}
		_shadowRanking.Sort((a, b) => a._shadowScore.CompareTo(b._shadowScore));

		int enabledCount = 0;
		bool pending = false;
		for (int i = 0; i < _shadowRanking.Count; i++)
		{
			Light light = _shadowRanking[i];
			if (i >= budget)
			{
				if (light._hasShadowBudget) light.SetShadowBudget(false);
			}
			else if (!light._hasShadowBudget)
			{
				if (enabledCount < 2)
				{
					light.SetShadowBudget(true);
					enabledCount++;
				}
				else
				{
					pending = true;
				}
			}
		}
		return pending;
	}

	private void SetShadowBudget(bool hasBudget)
	{
		_hasShadowBudget = hasBudget;
		GDLight.ShadowEnabled = hasBudget && Shadows && GetShadowQuality() != ShadowQuality.Off;
	}

	private float GetRange()
	{
		return GDLight is OmniLight3D omni ? omni.OmniRange : ((SpotLight3D)GDLight).SpotRange;
	}

	private static ShadowQuality GetShadowQuality()
	{
		ISettingsContext? settings =
#if CREATOR
			(ISettingsContext?)CreatorSettingsService.Instance ??
#endif
			ClientSettingsService.Instance;
		return settings?.Get<ShadowQuality>(SharedSettingKeys.Graphics.ShadowQuality) ?? ShadowQuality.Medium;
	}

	private void OnResourceLoaded(Resource? r)
	{
		if (r is Texture2D tex)
		{
			if (_prevImg == tex) return;
			_prevImg = tex;
			GDLight.LightProjector = tex;
		}
		else
		{
			_prevImg = null;
			GDLight.LightProjector = null;
		}
	}

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(new SpatialIcon(ClassName), @internal: Node.InternalMode.Back);
#endif
		ShadowSettingsChanged += UpdateShadows;
		UpdateFade();
		base.Init();
	}

	public override void PreDelete()
	{
		ShadowSettingsChanged -= UpdateShadows;
		_shadowCasters.Remove(this);
		base.PreDelete();
	}

	public static void NotifyShadowSettingsChanged()
	{
		ShadowSettingsChanged?.Invoke();
	}
}
