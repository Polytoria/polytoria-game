// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Enums;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Sound : Instance
{
	public const float SoundDistanceMultipler = 1.25f;
	private const float MinPitch = 0.001f;
	private const float MaxVolume = 2f;
	private AudioStreamPlayer? _audioPlayer;
	private AudioStreamPlayer3D? _audioPlayer3D;
	private bool _playAfterLoad = false;
	private bool _serverIsPlaying = false;
	private Resource? _prevAsset;
	private string _audioBusName = "Master";
	private AudioEffectPanner? _efPanner;

	private AudioAsset? _asset;
	private int _soundID = 0;
	private float _volume = 1f;
	private float _time = 0f;
	private bool _loop = false;
	private NumberRange _loopRange = new(-1, -1);
	private bool _inWorld = false;
	private float _lastPlaybackPos = 0f;
	private bool _playing = false;
	private bool _paused = false;
	private float _pitch = 1f;
	private NumberRange _distance = new(1f, 60f);
	private float _pan = 0f;

	private AudioStream? _currentStream;
	private decimal _playStartTime = 0;

	[Editable, ScriptProperty]
	public AudioAsset? Audio
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;

			_audioPlayer?.Stream = null;
			_audioPlayer3D?.Stream = null;
			_prevAsset = null;

			if (_asset != null)
			{
				Loading = true;
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;

				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use 'Audio' instead."), CloneIgnore]
	public int SoundID
	{
		get => _soundID;
		set
		{
			_soundID = value;
			CreatePTAudioAsset();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Volume
	{
		get => _volume;
		set
		{
			_volume = Mathf.Clamp(value, 0, MaxVolume);
			UpdateVolume();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Pitch
	{
		get => _pitch;
		set
		{
			_pitch = Mathf.Max(value, MinPitch);
			UpdatePitch();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Pan
	{
		get => _pan;
		set
		{
			_pan = Mathf.Clamp(value, -1f, 1f);
			UpdatePan();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Loop
	{
		get => _loop;
		set
		{
			_loop = value;

			SetStreamLoop(_currentStream, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Use 'Playing' instead.")]
	public bool Autoplay
	{
		get => Playing;
		set => Playing = value;
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Use 'LoopRange.Min' instead.")]
	public float LoopStart
	{
		get => LoopRange.Min;
		set => LoopRange = new(value, LoopRange.Max);
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Playback location is now determined by the sound's parent.")]
	public bool PlayInWorld
	{
		get => _inWorld;
		set { }
	}

	[Editable, ScriptProperty]
	public bool Paused
	{
		get => _paused;
		set
		{
			_paused = value;
			_audioPlayer?.StreamPaused = value;
			_audioPlayer3D?.StreamPaused = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Playing
	{
		get => _playing;
		set
		{
			if (_playing == value) return;

			if (Root != null && Root.SessionType == World.SessionTypeEnum.Creator)
			{
				_playing = value;
				OnPropertyChanged();
				return;
			}

			if (value) Play(); else Stop();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange LoopRange
	{
		get => _loopRange;
		set
		{
			_loopRange = value;
			if (_currentStream != null && value.Min >= 0)
			{
				SetStreamLoopStart(_currentStream, (float)Mathf.Clamp(value.Min, 0, _currentStream.GetLength()));
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public NumberRange Distance
	{
		get => _distance;
		set
		{
			_distance = value;
			UpdateDistance();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Use 'Distance.Max' instead.")]
	public float MaxDistance
	{
		get => Distance.Max;
		set => Distance = new(Distance.Min, value);
	}

	private AudioStreamPlayer3D.AttenuationModelEnum _attenuationMode = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;

	[Editable, ScriptProperty]
	public SoundAttenuationModeEnum AttenuationMode
	{
		get
		{
			return _attenuationMode switch
			{
				AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance => SoundAttenuationModeEnum.Inverse,
				AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance => SoundAttenuationModeEnum.Squared,
				AudioStreamPlayer3D.AttenuationModelEnum.Logarithmic => SoundAttenuationModeEnum.Logarithmic,
				AudioStreamPlayer3D.AttenuationModelEnum.Disabled => SoundAttenuationModeEnum.Disabled,
				_ => SoundAttenuationModeEnum.Disabled
			};
		}
		set
		{
			if (_audioPlayer3D == null) return;

			_attenuationMode = value switch
			{
				SoundAttenuationModeEnum.Inverse => AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
				SoundAttenuationModeEnum.Squared => AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance,
				SoundAttenuationModeEnum.Logarithmic => AudioStreamPlayer3D.AttenuationModelEnum.Logarithmic,
				SoundAttenuationModeEnum.Disabled => AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
				_ => _audioPlayer3D.AttenuationModel
			};

			_audioPlayer3D.AttenuationModel = _attenuationMode;
			_audioPlayer3D.AttenuationFilterCutoffHz = _attenuationMode == AudioStreamPlayer3D.AttenuationModelEnum.Disabled ? 20500 : 5000;

			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float Time
	{
		get => _audioPlayer != null ? _audioPlayer.GetPlaybackPosition() : _audioPlayer3D != null ? _audioPlayer3D.GetPlaybackPosition() : 0;
		set
		{
			_time = value;
			InternalSeek(_time);
			Rpc(nameof(NetSoundSeek), _time);
		}
	}

	[ScriptProperty] public bool Loading { get; private set; } = false;

	[ScriptProperty]
	public float Length => (_currentStream != null ? (float)_currentStream.GetLength() : 0);

	[ScriptProperty]
	public float Loudness
	{
		get
		{
			int bus = AudioServer.GetBusIndex(_audioBusName);
			if (bus < 0) return 0f;

			float left = AudioServer.GetBusPeakVolumeLeftDb(bus, 0);
			float right = AudioServer.GetBusPeakVolumeRightDb(bus, 0);
			return Mathf.DbToLinear(Mathf.Max(left, right));
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use 'Loudness' instead.")]
	public float GetPeakVolume() => Loudness;


	[ScriptProperty] public PTSignal Loaded { get; private set; } = new();
	[ScriptProperty] public PTSignal<bool> Played { get; private set; } = new();
	[ScriptProperty] public PTSignal<bool> Finished { get; private set; } = new();
	[ScriptProperty] public PTSignal Looped { get; private set; } = new();

	[SyncVar]
	public bool ServerIsPlaying
	{
		get => _serverIsPlaying;
		set
		{
			_serverIsPlaying = value;
			OnPropertyChanged();
		}
	}

	[SyncVar(ServerOnly = true)]
	internal decimal PlayStartTime
	{
		get => _playStartTime;
		set
		{
			_playStartTime = value;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		CreateAudioPlayer();
		SetProcess(true);
		base.Init();
	}

	public override void PostReparent()
	{
		bool inWorld = Parent is Physical;
		if (inWorld != _inWorld)
		{
			_inWorld = inWorld;
			CreateAudioPlayer();
		}
		base.PostReparent();
	}

	public override void PreDelete()
	{
		CleanupAudioPlayer();
		base.PreDelete();
	}

	private void CreateAudioPlayer()
	{
		_audioPlayer?.QueueFree();
		_audioPlayer3D?.QueueFree();

		CleanupAudioPlayer();

		if (!_inWorld)
		{
			_audioBusName = "Sound_" + ObjectID;
			AudioServer.AddBus();
			int idx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(idx, _audioBusName);
			AudioServer.SetBusSend(idx, "Master");
			_efPanner = new AudioEffectPanner();
			AudioServer.AddBusEffect(idx, _efPanner);

			_audioPlayer = new AudioStreamPlayer
			{
				Stream = _currentStream
			};
			GDNode.AddChild(_audioPlayer, @internal: Node.InternalMode.Back);
			_audioPlayer.Finished += OnPlayerFinished;
			_audioPlayer.Bus = _audioBusName;
		}
		else
		{
			_audioPlayer3D = new AudioStreamPlayer3D
			{
				Stream = _currentStream,
				AttenuationModel = _attenuationMode,
				AttenuationFilterCutoffHz = _attenuationMode == AudioStreamPlayer3D.AttenuationModelEnum.Disabled ? 20500 : 5000
			};
			GDNode.AddChild(_audioPlayer3D, @internal: Node.InternalMode.Back);
			_audioPlayer3D.Finished += OnPlayerFinished;
		}
		UpdateDistance();
		UpdateVolume();
		UpdatePitch();
	}

	private void CleanupAudioPlayer()
	{
		_audioPlayer?.Finished -= OnPlayerFinished;
		_audioPlayer3D?.Finished -= OnPlayerFinished;

		_audioPlayer = null;
		_audioPlayer3D = null;

		if (_audioBusName != "Master")
		{
			int idx = AudioServer.GetBusIndex(_audioBusName);

			if (idx >= 0) AudioServer.RemoveBus(idx);

			_efPanner = null;
		}
	}

	private void UpdateDistance()
	{
		if (_audioPlayer3D == null) return;
		_audioPlayer3D.UnitSize = _distance.Min;
		_audioPlayer3D.MaxDistance = _distance.Max * SoundDistanceMultipler;
	}

	private void UpdateVolume()
	{
		_audioPlayer?.VolumeLinear = _volume;
		_audioPlayer3D?.VolumeLinear = _volume;
	}

	private void UpdatePitch()
	{
		_audioPlayer?.PitchScale = _pitch;
		_audioPlayer3D?.PitchScale = _pitch;
	}

	private void UpdatePan()
	{
		// Pan does not apply to in-world sounds
		_efPanner?.Pan = _pan;
	}

	private void CreatePTAudioAsset()
	{
		Loading = true;
		PTAudioAsset audioAsset = new()
		{
			Name = "AudioAsset"
		};
		Audio = audioAsset;
		audioAsset.AudioID = (uint)_soundID;
	}

	public override void Process(double delta)
	{
		if (!Playing || Paused || !Loop) return;

		float pos = Time;
		if (_loopRange.Max >= 0 && pos >= _loopRange.Max)
		{
			InternalSeek(Mathf.Max(_loopRange.Min, 0));
			Looped.Invoke();
		}
		else
		{
			if (pos < _lastPlaybackPos) Looped.Invoke();
			_lastPlaybackPos = pos;
		}
		base.Process(delta);
	}

	private void OnPlayerFinished()
	{
		SetPlayingInternal(false);
		if (HasAuthority)
		{
			ServerIsPlaying = false;
		}
		Finished.Invoke(true);
	}

	[ScriptMethod]
	public void Play()
	{
		if (Paused)
		{
			Paused = false;
			Played.Invoke(true);
			return;
		}
		InternalPlay();
		Played.Invoke(false);

		if (HasAuthority)
		{
			Rpc(nameof(NetSoundPlay));
		}
	}

	[ScriptMethod]
	public void PlayOnce(float volume = 1f)
	{
		// WARN: only add panning to oneshot after sorting extra complexity of audiobus and safety
		InternalPlayOnce(volume);

		if (HasAuthority)
		{
			Rpc(nameof(NetPlayOnce), volume);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use 'PlayOnce' instead.")]
	public void PlayOneShot(float volume = 1f) => PlayOnce(volume);

	[ScriptMethod]
	public void Pause()
	{
		Paused = true;
	}

	[ScriptMethod]
	public void Stop()
	{
		InternalStop();

		if (HasAuthority)
		{
			Rpc(nameof(NetSoundStop));
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetPlayOnce(float volume)
	{
		Mathf.Clamp(volume, 0f, 1f);

		InternalPlayOnce(volume);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetSoundSeek(float to)
	{
		InternalSeek(to);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSoundPlay()
	{
		if (Root.SessionType != World.SessionTypeEnum.Client) { return; }
		InternalPlay();
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSoundStop()
	{
		InternalStop();
	}

	private void SetPlayingInternal(bool value)
	{
		_playing = value;
		OnPropertyChanged(nameof(Playing));
	}

	private void InternalPlay()
	{
		if (Root.SessionType == World.SessionTypeEnum.Creator) return;

		if (!Loading && Audio != null)
		{
			SetPlayingInternal(true);
			_lastPlaybackPos = 0f;
			if (HasAuthority)
			{
				ServerIsPlaying = true;
				PlayStartTime = Root.ServerTime;
			}
			_audioPlayer?.Play();
			_audioPlayer3D?.Play();
		}
		else
		{
			_playAfterLoad = true;
		}
	}

	private void InternalPlayOnce(float volume)
	{
		// can safely mute on the server since this method doesn't change any properties
		if (Root.Network.IsServer) return;

		if (_audioPlayer != null)
		{
			AudioStreamPlayer clone = (AudioStreamPlayer)_audioPlayer.Duplicate();
			GDNode.AddChild(clone, @internal: Node.InternalMode.Back);

			clone.Stream = _audioPlayer.Stream;
			clone.VolumeLinear = volume;

			void f()
			{
				clone.Finished -= f;
				clone.QueueFree();
			}

			clone.Finished += f;

			SetStreamLoop(clone.Stream, false);
			clone.Play();
		}

		if (_audioPlayer3D != null)
		{
			AudioStreamPlayer3D clone3D = (AudioStreamPlayer3D)_audioPlayer3D.Duplicate();
			GDNode.AddChild(clone3D, @internal: Node.InternalMode.Back);

			clone3D.Stream = _audioPlayer3D.Stream;
			clone3D.VolumeLinear = volume;

			void f()
			{
				clone3D.Finished -= f;
				clone3D.QueueFree();
			}

			clone3D.Finished += f;

			SetStreamLoop(clone3D.Stream, false);
			clone3D.Play();
		}
	}

	private void InternalStop()
	{
		bool wasPlaying = Playing;
		SetPlayingInternal(false);
		if (HasAuthority)
		{
			ServerIsPlaying = false;
		}
		_audioPlayer?.Stop();
		_audioPlayer3D?.Stop();
		if (wasPlaying)
		{
			Finished.Invoke(false);
		}
	}

	private void InternalSeek(float to)
	{
		_audioPlayer?.Seek(to);
		_audioPlayer3D?.Seek(to);
		_lastPlaybackPos = to;
	}

	private void OnResourceLoaded(Resource audio)
	{
		// Prevent the same resource firing twice
		if (audio == _prevAsset) return;
		_prevAsset = audio;
		Loading = false;
		_currentStream = (AudioStream)audio;
		_audioPlayer?.Stream = (AudioStream)audio;
		_audioPlayer3D?.Stream = (AudioStream)audio;

		// Re-apply to new stream
		LoopRange = _loopRange;
		Loop = _loop;

		Loaded.Invoke();

		if (_playAfterLoad || ServerIsPlaying)
		{
			_playAfterLoad = false;
			InternalPlay();

			// Catch up
			float elapsed = (float)(Root.ServerTime - PlayStartTime);
			if (Playing && elapsed > 0.05f && Length > 0)
			{
				InternalSeek(Loop ? elapsed % Length : Mathf.Min(elapsed, Length));
			}
		}
	}

	private static void SetStreamLoop(AudioStream? stream, bool val)
	{
		switch (stream)
		{
			case AudioStreamMP3 aStream:
				aStream.Loop = val;
				break;
			case AudioStreamOggVorbis aStream:
				aStream.Loop = val;
				break;
				// unused in Polytoria
				//case AudioStreamWav aStream:
		}
	}

	private static void SetStreamLoopStart(AudioStream? stream, float val)
	{
		switch (stream)
		{
			case AudioStreamMP3 aStream:
				aStream.LoopOffset = val;
				break;
			case AudioStreamOggVorbis aStream:
				aStream.LoopOffset = val;
				break;
				// unused in Polytoria
				//case AudioStreamWav aStream:
		}
	}
}
