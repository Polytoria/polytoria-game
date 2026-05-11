// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Scripting;

#if CREATOR
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Sound : Dynamic
{
	public const float SoundDistanceMultipler = 1.25f;
	private const float MinPitch = 0.001f;
	private AudioAsset? _asset;
	private AudioStreamPlayer? _audioPlayer;
	private AudioStreamPlayer3D? _audioPlayer3D;
	private bool _playAfterLoad = false;
	private bool _serverIsPlaying = false;
	private Resource? _prevAsset;

	private int _soundID = 0;
	private bool _autoplay = false;
	private float _volume = 1;
	private float _time = 0;
	private bool _loop = false;
	private bool _playInWorld = false;
	private bool _paused = false;
	private float _pitch = 1f;
	private float _maxDistance = 60f;

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
			_volume = Mathf.Clamp(value, 0, 1);
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
	public float Length => _audioPlayer != null
				? (float)_audioPlayer.Stream.GetLength()
				: _audioPlayer3D != null ? (float)_audioPlayer3D.Stream.GetLength() : 0;

	[ScriptProperty] public PTSignal Loaded { get; private set; } = new();

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
			_audioPlayer = new AudioStreamPlayer
			{
				Stream = _currentStream
			};
			GDNode.AddChild(_audioPlayer, @internal: Node.InternalMode.Back);
			_audioPlayer.Finished += OnPlayerFinished;
		}
		else
		{
			_audioPlayer3D = new AudioStreamPlayer3D
			{
				Stream = _currentStream
			};
			GDNode.AddChild(_audioPlayer3D, @internal: Node.InternalMode.Back);
			// check issue https://github.com/godotengine/godot/issues/23485
			_audioPlayer3D.AttenuationFilterCutoffHz = 20500;
			_audioPlayer3D.Finished += OnPlayerFinished;
		}
		UpdateAudioPlayer();
	}

	private void CleanupAudioPlayer()
	{
		if (_audioPlayer != null)
		{
			_audioPlayer.Finished -= OnPlayerFinished;
		}

		if (_audioPlayer3D != null)
		{
			_audioPlayer3D.Finished -= OnPlayerFinished;
		}

		_audioPlayer = null;
		_audioPlayer3D = null;
	}

	private void UpdateAudioPlayer()
	{
		UpdateMaxDistance();
		UpdateVolume();
		UpdatePitch();
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
		// Loop the audio
		if (Loop)
		{
			Play();
		}
		else
		{
			Playing = false;
			if (HasAuthority)
			{
				ServerIsPlaying = false;
			}
		}
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
		if (volume > 1)
		{
			volume = 1;
		}

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
			// Mute audio on server
			if (Root.Network.IsServer) return;
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

		Loaded.Invoke();

		if (Autoplay || _playAfterLoad || ServerIsPlaying)
		{
			_playAfterLoad = false;
			InternalPlay();
		}
	}
}
