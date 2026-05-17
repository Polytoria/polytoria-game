// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings.Appliers;

public sealed partial class DisplaySettingsApplier : Node
{
	public override void _Ready()
	{
		ClientSettingsService.Instance.Changed += OnChanged;
		ApplyAll();
	}

	public override void _ExitTree()
	{
		ClientSettingsService.Instance.Changed -= OnChanged;
		base._ExitTree();
	}

	private void OnChanged(SettingChangedEvent change)
	{
		switch (change.Key)
		{
			case SharedSettingKeys.Display.Fullscreen:
				ApplyFullscreen();
				break;
			case SharedSettingKeys.Display.VSync:
				ApplyVsync();
				break;
			case ClientSettingKeys.Display.UiScale:
				ApplyUiScale();
				break;
		}
	}

	private void ApplyAll()
	{
		ApplyFullscreen();
		ApplyVsync();
		ApplyUiScale();
	}

	private void ApplyFullscreen()
	{
		bool fullscreen = ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.Fullscreen);
		var defaultMode = DisplayServer.WindowMode.Maximized;
		if (Globals.IsInGDEditor)
		{
			defaultMode = DisplayServer.WindowMode.Windowed;
		}
		DisplayServer.WindowSetMode(fullscreen ? DisplayServer.WindowMode.Fullscreen : defaultMode);
	}

	private void ApplyVsync()
	{
		bool vsync = ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.VSync);
		DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
	}

	private void ApplyUiScale()
	{
		float scale = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Display.UiScale);
		float finalScale;
		int screenId = DisplayServer.WindowGetCurrentScreen();
		float osScale = DisplayServer.ScreenGetScale(screenId);
		finalScale = scale * osScale;
		GetTree().Root.ContentScaleFactor = finalScale;
	}
}
