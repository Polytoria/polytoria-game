// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel.Services;

[Static("Audio")]
public sealed partial class AudioService : Instance
{
	[ScriptProperty]
	public float MasterVolume
	{
		get => Mathf.DbToLinear(AudioServer.GetBusVolumeDb(0));
		set
		{
			AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Clamp(value, 0f, 1f)));
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float PlaybackSpeedScale
	{
		get => AudioServer.PlaybackSpeedScale;
		set
		{
			AudioServer.PlaybackSpeedScale = Mathf.Max(value, 0.01f);
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float TimeSinceLastMix => (float)AudioServer.GetTimeSinceLastMix();

	[ScriptProperty]
	public float TimeToNextMix => (float)AudioServer.GetTimeToNextMix();

	[ScriptProperty]
	public float OutputLatency => (float)AudioServer.GetOutputLatency();

	[ScriptProperty]
	public float MixRate => AudioServer.GetMixRate();

	[ScriptProperty]
	public int BusCount => AudioServer.BusCount;

	[ScriptProperty]
	public string DriverName => AudioServer.GetDriverName();

	[ScriptProperty]
	public SpeakerModeEnum SpeakerMode => (int)AudioServer.GetSpeakerMode() switch
	{
		0 => SpeakerModeEnum.Stereo,
		1 => SpeakerModeEnum.Surround31,
		2 => SpeakerModeEnum.Surround51,
		3 => SpeakerModeEnum.Surround71,
		_ => SpeakerModeEnum.Stereo
	};

	public enum SpeakerModeEnum
	{
		Stereo,
		Surround31,
		Surround51,
		Surround71
	}
}
