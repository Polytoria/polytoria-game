// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;

#if CREATOR
using Polytoria.Creator.Settings;
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Abstract]
public partial class Light : Dynamic
{
	const float IntensityConversion = 4f;
	internal Light3D GDLight = null!;
	private bool _enabled = true;
	private Color _color = new(1, 1, 1);
	private float _brightness = 2;
	private float _lightSize = 0;
	private float _specular = 0.5f;
	private bool _shadows = false;

	private static Action? ShadowSettingsChanged;

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

	internal void UpdateShadows()
	{
		bool shadows = Shadows;

		ISettingsContext? settings =
#if CREATOR
			(ISettingsContext?)CreatorSettingsService.Instance ??
#endif
			ClientSettingsService.Instance;
		if (settings != null)
		{
			ShadowQuality shadowQuality = settings.Get<ShadowQuality>(SharedSettingKeys.Graphics.ShadowQuality);
			if (shadowQuality == ShadowQuality.Off)
			{
				shadows = false;
			}
		}
		GDLight.ShadowEnabled = shadows;
	}

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(new SpatialIcon(ClassName), @internal: Node.InternalMode.Back);
#endif
		ShadowSettingsChanged += UpdateShadows;
		base.Init();
	}

	public static void NotifyShadowSettingsChanged()
	{
		ShadowSettingsChanged?.Invoke();
	}
}
