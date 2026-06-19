// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Enums;


#if CREATOR
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Sound : Dynamic
{
	public const float SoundDistanceMultipler = 1.25f;
	private const float MinPitch = 0.001f;
	private const float MaxVolume = 2f;
	private static int _counter = 0;
	private AudioStreamPlayer? _audioPlayer;
	private AudioStreamPlayer3D? _audioPlayer3D;
	private bool _playAfterLoad = false;
	private bool _serverIsPlaying = false;
	private Resource? _prevAsset;
	private string _audioBusName = "Master";
	private AudioEffectPanner? _efPanner;
	private int _id = System.Threading.Interlocked.Increment(ref _counter);

	private AudioAsset? _asset;
	private int _soundID = 0;
	private bool _autoplay = false;
	private float _volume = 1f;
	private float _time = 0f;
	private bool _loop = false;
	private float _loopStart = 0f;
	private bool _playInWorld = false;
	private bool _paused = false;
	private float _pitch = 1f;
	private float _maxDistance = 60f;
	private float _pan = 0f;

	private AudioStream? _currentStream;

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

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Audio instead"), CloneIgnore]
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
	public bool Autoplay
	{
		get => _autoplay;
		set
		{
			_autoplay = value;
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

	[Editable, ScriptProperty]
	public float LoopStart
	{
		get => _loopStart;
		set
		{
			// unclamped value is reapplied and clamped when Sound is loaded
			if (_currentStream != null)
			{
				value = (float)Mathf.Clamp(value, 0, _currentStream.GetLength());
			}

			_loopStart = value;

			SetStreamLoopStart(_currentStream, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool PlayInWorld
	{
		get => _playInWorld;
		set
		{
			_playInWorld = value;
			CreateAudioPlayer();
			OnPropertyChanged();
		}
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
	public float MaxDistance
	{
		get => _maxDistance;
		set
		{
			_maxDistance = value;
			UpdateMaxDistance();
		}
	}

	private AudioStreamPlayer3D.AttenuationModelEnum _attenuationMode = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;

	[Editable, ScriptProperty]
	public SoundAttenuationModeEnum AttenuationMode
	{
		get
		{
			return _attenuationMode switch
			{
				AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance => SoundAttenuationModeEnum.Linear,
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
				SoundAttenuationModeEnum.Linear => AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
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

			if (HasAuthority)
			{
				Rpc(nameof(NetSoundSeek), _time);
			}
		}
	}

	[ScriptProperty] public bool Playing { get; private set; } = false;
	[ScriptProperty] public bool Loading { get; private set; } = false;

	[ScriptProperty]
	public float Length => (_currentStream != null ? (float)_currentStream.GetLength() : 0);

	[ScriptProperty] public PTSignal Loaded { get; private set; } = new();
	[ScriptProperty] public PTSignal Finished { get; private set; } = new();

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

	public override void Init()
	{
		CreateAudioPlayer();
#if CREATOR
		GDNode.AddChild(new SpatialIcon(ClassName), @internal: Node.InternalMode.Back);
#endif
		base.Init();
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

		if (!PlayInWorld)
		{
			_audioBusName = $"Sound_{_id}";
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
		UpdateMaxDistance();
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

	private void UpdateMaxDistance()
	{
		_audioPlayer3D?.MaxDistance = _maxDistance * SoundDistanceMultipler;
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

	private void OnPlayerFinished()
	{
		// Event is not fired on looping sound
		Playing = false;
		if (HasAuthority)
		{
			ServerIsPlaying = false;
		}
		Finished.Invoke();
	}

	[ScriptMethod]
	public void Play()
	{
		if (Paused)
		{
			Paused = false;
			return;
		}
		InternalPlay();

		if (HasAuthority)
		{
			Rpc(nameof(NetSoundPlay));
		}
	}

	[ScriptMethod]
	public void PlayOneShot(float volume = 1f)
	{
		// WARN: only add panning to oneshot after sorting extra complexity of audiobus and safety
		InternalPlayOneShot(volume);

		if (HasAuthority)
		{
			Rpc(nameof(NetPlayOneshot), volume);
		}
	}

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
	private void NetPlayOneshot(float volume)
	{
		Mathf.Clamp(volume, 0f, 1f);

		InternalPlayOneShot(volume);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
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

	private void InternalPlay()
	{
		if (Root.SessionType == World.SessionTypeEnum.Creator) return;

		if (!Loading && Audio != null)
		{
			Playing = true;
			if (HasAuthority)
			{
				ServerIsPlaying = true;
			}
			_audioPlayer?.Play();
			_audioPlayer3D?.Play();
		}
		else
		{
			_playAfterLoad = true;
		}
	}

	private void InternalPlayOneShot(float volume)
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
		Playing = false;
		if (HasAuthority)
		{
			ServerIsPlaying = false;
		}
		_audioPlayer?.Stop();
		_audioPlayer3D?.Stop();
	}

	private void InternalSeek(float to)
	{
		_audioPlayer?.Seek(to);
		_audioPlayer3D?.Seek(to);
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
		// reapply to new stream
		LoopStart = _loopStart;
		Loop = _loop;

		Loaded.Invoke();

		if (Autoplay || _playAfterLoad || ServerIsPlaying)
		{
			_playAfterLoad = false;
			InternalPlay();
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
