// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using Polytoria.Attributes;
using Polytoria.Scripting;
using Polytoria.Shared.Settings;
using Polytoria.Client.Settings;
using Polytoria.Enums;

namespace Polytoria.Datamodel.Services;

[Static("Preferences")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class PreferencesService : Instance
{
	[ScriptProperty] public PTSignal<string, object> SettingChanged { get; private set; } = new();
	[ScriptProperty] public float CameraSensitivity => ClientSettingsService.Instance.Get<float>(ClientSettingKeys.General.CameraSensitivity);
	[ScriptProperty] public float UiScale => ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Display.UiScale);

	[ScriptLegacyProperty("UsePhotoMode")] public static bool LegacyUsePhotoMode => ClientSettingsService.Instance.Get<GraphicsPreset>(SharedSettingKeys.Graphics.Preset) == GraphicsPreset.Photo;
	// We might want to check if all the post processing is enabled rather than just one.
	[ScriptLegacyProperty("UsePostProcessing")] public static bool UsePostProcessing => ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.PostProcessing.Glow) == true;

	private static readonly Dictionary<string, Type> SettingEnumTypes = new()
	{
		[SharedSettingKeys.Graphics.Preset] = typeof(GraphicsPresetEnum),
		[SharedSettingKeys.Graphics.RenderingMethod] = typeof(RenderingMethodEnum),
		[SharedSettingKeys.Graphics.ShadowQuality] = typeof(ShadowQualityEnum),
		[SharedSettingKeys.Graphics.Msaa] = typeof(MsaaScaleEnum),
	};

	[ScriptMethod]
	public object? Get(string name)
	{
		return ClientSettingsService.Instance.GetUntyped(name);
	}

	public override void Init()
	{
		if (ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed += OnSettingChanged;
		}
		base.Init();
	}

	public override void PreDelete()
	{
		if (ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed -= OnSettingChanged;
		}
		base.PreDelete();
	}

	private void OnSettingChanged(SettingChangedEvent setting)
	{
		object value = setting.NewValue;

		if (SettingEnumTypes.TryGetValue(setting.Key, out var enumType))
		{
			value = Enum.ToObject(enumType, setting.NewValue);
		}

		SettingChanged.Invoke(setting.Key, value);
	}
}
