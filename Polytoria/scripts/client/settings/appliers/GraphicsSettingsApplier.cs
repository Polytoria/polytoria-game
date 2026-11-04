// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings.Appliers;

public sealed partial class GraphicsSettingsApplier : Node
{
	public const string NodeName = "GraphicsSettingsApplier";

	public ISettingsContext? Settings { get; set; }
	private bool _postProcessingDirty;

	public Viewport? RenderViewport { get; set; }

	public override void _Ready()
	{
		Settings?.Changed += OnChanged;

		ApplyPostProcessing();
		ApplyViewportSettings();
	}

	public void ApplyViewportSettings()
	{
		ApplyRenderScale();
		ApplyMsaa();
		ApplyScreenSpaceAA();
		ApplyShadowQuality();
		ApplyShadowDistance();
	}

	public override void _ExitTree()
	{
		Settings?.Changed -= OnChanged;
		base._ExitTree();
	}

	private Viewport GetRenderViewport()
	{
		return RenderViewport ?? GetViewport();
	}

	private void OnChanged(SettingChangedEvent change)
	{
		if (change.Key.StartsWith(SharedSettingKeys.PostProcessing.Prefix))
		{
			if (!_postProcessingDirty)
			{
				_postProcessingDirty = true;
				Callable.From(() =>
				{
					_postProcessingDirty = false;
					ApplyPostProcessing();
				}).CallDeferred();
			}
		}
		else
		{
			switch (change.Key)
			{
				case SharedSettingKeys.Graphics.RenderScale:
					ApplyRenderScale();
					break;
				case SharedSettingKeys.Graphics.Msaa:
					ApplyMsaa();
					break;
				case SharedSettingKeys.Graphics.ScreenSpaceAA:
					ApplyScreenSpaceAA();
					break;
				case SharedSettingKeys.Graphics.ShadowQuality:
					ApplyShadowQuality();
					break;
				case SharedSettingKeys.Graphics.ShadowDistance:
					ApplyShadowDistance();
					break;
			}
		}
	}

	private void ApplyPostProcessing()
	{
		ApplyNormalMaps();
		ApplyEffectsQuality();

		World? world = World.Current;
		if (world?.Lighting == null)
		{
			return;
		}

		world.Lighting.ApplyGraphicsSettings(Settings!);
	}

	private void ApplyNormalMaps()
	{
		bool enabled = Settings!.Get<bool>(SharedSettingKeys.PostProcessing.NormalMaps) && SharedSettingsRegistry.IsSupported(SharedSettingKeys.PostProcessing.NormalMaps);
		Globals.SetNormalMapsEnabled(enabled);
	}

	private void ApplyRenderScale()
	{
		float renderScale = Settings!.Get<float>(SharedSettingKeys.Graphics.RenderScale);
		GetRenderViewport().Scaling3DScale = renderScale;
	}

	private void ApplyScreenSpaceAA()
	{
		if (!SharedSettingsRegistry.IsSupported(SharedSettingKeys.Graphics.ScreenSpaceAA)) return;

		ScreenSpaceAAOption aa = Settings!.Get<ScreenSpaceAAOption>(SharedSettingKeys.Graphics.ScreenSpaceAA);
		GetRenderViewport().ScreenSpaceAA = aa switch
		{
			ScreenSpaceAAOption.Fxaa => Viewport.ScreenSpaceAAEnum.Fxaa,
			ScreenSpaceAAOption.Smaa => Viewport.ScreenSpaceAAEnum.Smaa,
			_ => Viewport.ScreenSpaceAAEnum.Disabled
		};
	}

	private void ApplyEffectsQuality()
	{
		if (!SharedSettingsRegistry.IsSupported(SharedSettingKeys.PostProcessing.EffectsQuality)) return;

		EffectsQualityOption quality = Settings!.Get<EffectsQualityOption>(SharedSettingKeys.PostProcessing.EffectsQuality);

		RenderingServer.EnvironmentSetSsaoQuality(
			quality switch
			{
				EffectsQualityOption.Low => RenderingServer.EnvironmentSsaoQuality.Low,
				EffectsQualityOption.High => RenderingServer.EnvironmentSsaoQuality.High,
				_ => RenderingServer.EnvironmentSsaoQuality.Medium
			},
			ProjectSettings.GetSetting("rendering/environment/ssao/half_size").AsBool(),
			ProjectSettings.GetSetting("rendering/environment/ssao/adaptive_target").AsSingle(),
			ProjectSettings.GetSetting("rendering/environment/ssao/blur_passes").AsInt32(),
			ProjectSettings.GetSetting("rendering/environment/ssao/fadeout_from").AsSingle(),
			ProjectSettings.GetSetting("rendering/environment/ssao/fadeout_to").AsSingle());

		if (!SharedSettingsRegistry.IsSupported(SharedSettingKeys.PostProcessing.Ssil)) return;

		RenderingServer.EnvironmentSetSsilQuality(
			quality switch
			{
				EffectsQualityOption.Low => RenderingServer.EnvironmentSsilQuality.Low,
				EffectsQualityOption.High => RenderingServer.EnvironmentSsilQuality.High,
				_ => RenderingServer.EnvironmentSsilQuality.Medium
			},
			ProjectSettings.GetSetting("rendering/environment/ssil/half_size").AsBool(),
			ProjectSettings.GetSetting("rendering/environment/ssil/adaptive_target").AsSingle(),
			ProjectSettings.GetSetting("rendering/environment/ssil/blur_passes").AsInt32(),
			ProjectSettings.GetSetting("rendering/environment/ssil/fadeout_from").AsSingle(),
			ProjectSettings.GetSetting("rendering/environment/ssil/fadeout_to").AsSingle());
	}

	private void ApplyMsaa()
	{
		MsaaOption msaa = Settings!.Get<MsaaOption>(SharedSettingKeys.Graphics.Msaa);
		Viewport viewport = GetRenderViewport();
		viewport.Msaa3D = msaa switch
		{
			MsaaOption.Disabled => Viewport.Msaa.Disabled,
			MsaaOption.X2 => Viewport.Msaa.Msaa2X,
			MsaaOption.X4 => Viewport.Msaa.Msaa4X,
			MsaaOption.X8 => Viewport.Msaa.Msaa8X,
			_ => viewport.Msaa3D
		};
	}

	private void ApplyShadowQuality()
	{
		ShadowQuality quality = Settings!.Get<ShadowQuality>(SharedSettingKeys.Graphics.ShadowQuality);

		RenderingServer.ShadowQuality gdQuality = quality switch
		{
			ShadowQuality.Off => RenderingServer.ShadowQuality.Hard,
			ShadowQuality.Low => RenderingServer.ShadowQuality.Hard,
			ShadowQuality.Medium => RenderingServer.ShadowQuality.SoftLow,
			ShadowQuality.High => RenderingServer.ShadowQuality.SoftMedium,
			ShadowQuality.Ultra => RenderingServer.ShadowQuality.SoftHigh,
			_ => RenderingServer.ShadowQuality.SoftMedium
		};

		int directionalShadowSize = quality switch
		{
			ShadowQuality.Off => 0,
			ShadowQuality.Low => 1024,
			ShadowQuality.Medium => 2048,
			ShadowQuality.High => 4096,
			ShadowQuality.Ultra => 8192,
			_ => 2048
		};

		int positionalShadowSize = quality switch
		{
			ShadowQuality.Off => 0,
			ShadowQuality.Low => 1024,
			ShadowQuality.Medium => 2048,
			ShadowQuality.High => 4096,
			ShadowQuality.Ultra => 4096,
			_ => 2048
		};

		RenderingServer.ShadowQuality positionalQuality = quality == ShadowQuality.Ultra ? RenderingServer.ShadowQuality.SoftMedium : gdQuality;

		RenderingServer.DirectionalSoftShadowFilterSetQuality(gdQuality);
		RenderingServer.PositionalSoftShadowFilterSetQuality(positionalQuality);

		RenderingServer.DirectionalShadowAtlasSetSize(directionalShadowSize, true);
		RenderingServer.ViewportSetPositionalShadowAtlasSize(GetRenderViewport().GetViewportRid(), positionalShadowSize, true);

		Light.NotifyShadowSettingsChanged();
	}

	private void ApplyShadowDistance()
	{
		World? world = World.Current;
		if (world == null || world.Lighting == null)
		{
			return;
		}

		SunLight sun = world.Lighting.Sun;
		DirectionalLight3D node = (DirectionalLight3D)sun.GDLight;

		float distance = Settings!.Get<float>(SharedSettingKeys.Graphics.ShadowDistance);
		node.DirectionalShadowMaxDistance = distance;
	}
}
