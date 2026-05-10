// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class SoundGroup : Instance
{
	private float _volume = 1f;
	private bool _muted = false;
	private bool _solo = false;
	private bool _paused = false;
	private string _busName = "";
	private readonly List<Sound> _sounds = [];
	private readonly HashSet<Sound> _pausedByGroup = [];

	private int Bus => _busName.Length > 0 ? AudioServer.GetBusIndex(_busName) : -1;

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Volume
	{
		get => _volume;
		set
		{
			_volume = Mathf.Clamp(value, 0f, 1f);
			int bus = Bus;
			if (bus >= 0)
				AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(_volume));
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float VolumeDb
	{
		get
		{
			int bus = Bus;
			return bus >= 0 ? AudioServer.GetBusVolumeDb(bus) : 0f;
		}
		set
		{
			_volume = Mathf.Clamp(Mathf.DbToLinear(value), 0f, 1f);
			int bus = Bus;
			if (bus >= 0)
				AudioServer.SetBusVolumeDb(bus, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Muted
	{
		get => _muted;
		set
		{
			_muted = value;
			int bus = Bus;
			if (bus >= 0)
				AudioServer.SetBusMute(bus, _muted);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Solo
	{
		get => _solo;
		set
		{
			_solo = value;
			int bus = Bus;
			if (bus >= 0)
				AudioServer.SetBusSolo(bus, _solo);
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float PeakVolume
	{
		get
		{
			int bus = Bus;
			if (bus < 0) return 0f;
			float left = AudioServer.GetBusPeakVolumeLeftDb(bus, 0);
			float right = AudioServer.GetBusPeakVolumeRightDb(bus, 0);
			float peak = Mathf.DbToLinear(Mathf.Max(left, right));
			return peak < 0.0001f ? 0f : peak;
		}
	}

	[ScriptProperty]
	public float PeakVolumeDb
	{
		get
		{
			int bus = Bus;
			if (bus < 0) return -80f;
			float left = AudioServer.GetBusPeakVolumeLeftDb(bus, 0);
			float right = AudioServer.GetBusPeakVolumeRightDb(bus, 0);
			return Mathf.Max(left, right);
		}
	}

	[ScriptProperty]
	public float PeakVolumeLeftDb
	{
		get
		{
			int bus = Bus;
			return bus >= 0 ? AudioServer.GetBusPeakVolumeLeftDb(bus, 0) : -80f;
		}
	}

	[ScriptProperty]
	public float PeakVolumeRightDb
	{
		get
		{
			int bus = Bus;
			return bus >= 0 ? AudioServer.GetBusPeakVolumeRightDb(bus, 0) : -80f;
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool BypassEffects
	{
		get
		{
			int bus = Bus;
			return bus >= 0 && AudioServer.IsBusBypassingEffects(bus);
		}
		set
		{
			int bus = Bus;
			if (bus >= 0)
				AudioServer.SetBusBypassEffects(bus, value);
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public int BusIndex => Bus;

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Paused
	{
		get => _paused;
		set
		{
			if (_paused == value) return;
			_paused = value;

			if (_paused)
			{
				_pausedByGroup.Clear();
				foreach (var sound in _sounds)
				{
					if (!sound.Paused && sound.Playing)
					{
						sound.Paused = true;
						_pausedByGroup.Add(sound);
					}
				}
			}
			else
			{
				foreach (var sound in _pausedByGroup)
					sound.Paused = false;
				_pausedByGroup.Clear();
			}

			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public void Pause()
	{
		Paused = true;
	}

	[ScriptMethod]
	public void Play()
	{
		Paused = false;
	}

	internal void RegisterSound(Sound sound)
	{
		if (!_sounds.Contains(sound))
			_sounds.Add(sound);

		if (_paused && sound.Playing && !sound.Paused)
		{
			sound.Paused = true;
			_pausedByGroup.Add(sound);
		}
	}

	internal void UnregisterSound(Sound sound)
	{
		_sounds.Remove(sound);
		_pausedByGroup.Remove(sound);
	}

	private AudioEffectSpectrumAnalyzerInstance? _spectrumInstance;
	private AudioEffectCapture? _capture;

	[ScriptMethod]
	public float GetMagnitudeForRange(float fromHz, float toHz)
	{
		if (_spectrumInstance == null)
			return 0f;

		Vector2 mag = _spectrumInstance.GetMagnitudeForFrequencyRange(fromHz, toHz);
		return (mag.X + mag.Y) * 0.5f;
	}

	[ScriptMethod]
	public void EnableSpectrum()
	{
		int bus = Bus;
		if (bus < 0 || _spectrumInstance != null)
			return;

		var analyzer = new AudioEffectSpectrumAnalyzer { BufferLength = 0.1f };
		int idx = AudioServer.GetBusEffectCount(bus);
		AudioServer.AddBusEffect(bus, analyzer);
		_spectrumInstance = (AudioEffectSpectrumAnalyzerInstance)
			AudioServer.GetBusEffectInstance(bus, idx);
	}

	[ScriptMethod]
	public void EnableCapture()
	{
		int bus = Bus;
		if (bus < 0 || _capture != null)
			return;

		_capture = new AudioEffectCapture { BufferLength = 0.1f };
		AudioServer.AddBusEffect(bus, _capture);
	}

	[ScriptProperty]
	public int CaptureFramesAvailable => _capture?.GetFramesAvailable() ?? 0;

	[ScriptMethod]
	public void ClearCaptureBuffer()
	{
		_capture?.ClearBuffer();
	}

	public override void Init()
	{
		int idx = AudioServer.BusCount;
		AudioServer.AddBus();
		_busName = "SG_" + Name + "_" + GetHashCode();
		AudioServer.SetBusName(idx, _busName);
		AudioServer.SetBusSend(idx, "Master");

		Volume = _volume;
		Muted = _muted;
		Solo = _solo;

		base.Init();
	}

	public override void PreDelete()
	{
		foreach (var sound in _sounds)
			sound.ClearSoundGroup();
		_sounds.Clear();
		_pausedByGroup.Clear();

		int bus = Bus;
		if (bus >= 0)
			AudioServer.RemoveBus(bus);
		_busName = "";
		base.PreDelete();
	}
}
