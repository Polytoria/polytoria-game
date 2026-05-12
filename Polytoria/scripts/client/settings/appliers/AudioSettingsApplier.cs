using Godot;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings.Appliers;

public sealed partial class AudioSettingsApplier : Node
{
	public override void _Ready()
	{
		ClientSettingsService.Instance.Changed += OnChanged;
		ApplyAll();
	}

	public override void _ExitTree()
	{
		if (ClientSettingsService.Instance != null)
			ClientSettingsService.Instance.Changed -= OnChanged;
		base._ExitTree();
	}

	private void OnChanged(SettingChangedEvent change)
	{
		switch (change.Key)
		{
			case ClientSettingKeys.General.MasterVolume:
				ApplyVolume();
				break;
		}
	}

	private void ApplyAll()
	{
		ApplyVolume();
	}

	private void ApplyVolume()
	{
		float volume = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.General.MasterVolume);
		AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(volume / 100f));
	}
}
