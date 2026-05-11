using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings.Appliers;

public sealed partial class GraphicsSettingsApplier : Node
{
	public override void _Ready()
	{
		ClientSettingsService.Instance.Changed += OnChanged;
		ApplyAll();
	}

	private void OnChanged(SettingChangedEvent change)
	{
		if (change.Key == ClientSettingKeys.PostProcessing.NormalMaps)
		{
			ApplyNormalMaps();
		}
		else if (change.Key.StartsWith("graphics.post_processing."))
		{
			ApplyPostProcessing();
		}
		else
		{
			switch (change.Key)
			{
				case ClientSettingKeys.Graphics.RenderScale:
					ApplyRenderScale();
					break;
				case ClientSettingKeys.Graphics.Msaa:
					ApplyMsaa();
					break;
				case ClientSettingKeys.Graphics.ShadowQuality:
					ApplyShadowQuality();
					break;
				case ClientSettingKeys.Graphics.ShadowDistance:
					ApplyShadowDistance();
					break;
			}
		}
	}

	private void ApplyPostProcessing()
	{
		World? world = World.Current;
		if (world?.Lighting == null)
		{
			return;
		}

		world.Lighting.ApplyGraphicsSettings();
	}

	private void ApplyNormalMaps()
	{
		bool enabled = ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.PostProcessing.NormalMaps);
		if (Globals.IsMobileBuild)
		{
			enabled = false;
		}
		Globals.SetNormalMapsEnabled(enabled);
	}

	private void ApplyAll()
	{
		ApplyPostProcessing();
		ApplyNormalMaps();
		ApplyRenderScale();
		ApplyMsaa();
		ApplyShadowQuality();
		ApplyShadowDistance();
	}

	private void ApplyRenderScale()
	{
		float renderScale = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Graphics.RenderScale);
		GetViewport().Scaling3DScale = renderScale;
	}

	private void ApplyMsaa()
	{
		MsaaOption msaa = ClientSettingsService.Instance.Get<MsaaOption>(ClientSettingKeys.Graphics.Msaa);
		GetViewport().Msaa3D = msaa switch
		{
			MsaaOption.Disabled => Viewport.Msaa.Disabled,
			MsaaOption.X2 => Viewport.Msaa.Msaa2X,
			MsaaOption.X4 => Viewport.Msaa.Msaa4X,
			MsaaOption.X8 => Viewport.Msaa.Msaa8X,
			_ => GetViewport().Msaa3D
		};
	}

	private void ApplyShadowQuality()
	{
		ShadowQuality quality = ClientSettingsService.Instance.Get<ShadowQuality>(ClientSettingKeys.Graphics.ShadowQuality);

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
		RenderingServer.ViewportSetPositionalShadowAtlasSize(GetViewport().GetViewportRid(), positionalShadowSize, false);

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
		DirectionalLight3D node = (DirectionalLight3D)sun.LightNode;

		float distance = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Graphics.ShadowDistance);
		node.DirectionalShadowMaxDistance = distance;
	}
}
