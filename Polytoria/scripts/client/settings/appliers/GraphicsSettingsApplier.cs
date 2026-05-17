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
		if (Settings != null)
			Settings.Changed += OnChanged;

		ApplyPostProcessing();
		ApplyViewportSettings();
	}

	public void ApplyViewportSettings()
	{
		ApplyRenderScale();
		ApplyMsaa();
		ApplyShadowQuality();
		ApplyShadowDistance();
	}

	public override void _ExitTree()
	{
		if (Settings != null)
			Settings.Changed -= OnChanged;
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

		World? world = World.Current;
		if (world?.Lighting == null)
		{
			return;
		}

		world.Lighting.ApplyGraphicsSettings(Settings!);
	}

	private void ApplyNormalMaps()
	{
		bool enabled = Settings!.Get<bool>(SharedSettingKeys.PostProcessing.NormalMaps);
		if (Globals.IsMobileBuild)
		{
			enabled = false;
		}
		Globals.SetNormalMapsEnabled(enabled);
	}

	private void ApplyRenderScale()
	{
		float renderScale = Settings!.Get<float>(SharedSettingKeys.Graphics.RenderScale);
		GetRenderViewport().Scaling3DScale = renderScale;
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

		RenderingServer.DirectionalSoftShadowFilterSetQuality(gdQuality);
		RenderingServer.PositionalSoftShadowFilterSetQuality(gdQuality);

		RenderingServer.DirectionalShadowAtlasSetSize(directionalShadowSize, false);
		RenderingServer.ViewportSetPositionalShadowAtlasSize(GetRenderViewport().GetViewportRid(), positionalShadowSize, false);

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
