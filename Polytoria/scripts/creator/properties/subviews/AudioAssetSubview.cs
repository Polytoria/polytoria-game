// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Creator.Properties;

public sealed partial class AudioAssetSubview : Control, IPropertySubview
{
	public NetworkedObject TargetObject { get; set; } = null!;

	private AudioAsset _baseAsset = null!;
	private Button _previewButton = null!;
	private HSlider _timeSlider = null!;
	private Texture2D _playIcon = null!;
	private Texture2D _pauseIcon = null!;

	private AudioStreamPlayer? _player;
	private bool _isPlaying;

	private Sound? _ownerSound;

	public override void _Ready()
	{
		_baseAsset = (AudioAsset)TargetObject;
		_previewButton = GetNode<Button>("PreviewButton");
		_timeSlider = GetNode<HSlider>("TimeSlider");

		_playIcon = GD.Load<Texture2D>("res://assets/textures/ui-icons/play-filled.svg");
		_pauseIcon = GD.Load<Texture2D>("res://assets/textures/ui-icons/pause-filled.svg");

		_previewButton.Text = "";
		_previewButton.Icon = _playIcon;

		_previewButton.Pressed += OnPreviewPressed;
		_timeSlider.ValueChanged += OnTimeChanged;

		_baseAsset.ResourceLoaded += OnResourceLoaded;

		_ownerSound = GetOwnerSound();

		if (_baseAsset.Resource is AudioStream stream)
		{
			_previewButton.Disabled = false;
			_timeSlider.Editable = true;
			_timeSlider.MaxValue = (float)stream.GetLength();
		}
		else
		{
			_timeSlider.Editable = false;
			_timeSlider.AddThemeStyleboxOverride("slider", new StyleBoxEmpty());
			_previewButton.Disabled = true;
		}

		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (_player == null || !_isPlaying) return;

		float length = (float)_player.Stream.GetLength();
		float pos = _player.GetPlaybackPosition();
		_timeSlider.MaxValue = length;
		_timeSlider.SetValueNoSignal(pos);
	}

	private void OnPreviewPressed()
	{
		if (_baseAsset.Resource is not AudioStream stream) return;

		if (_isPlaying)
			StopPreview();
		else
			StartPreview(stream);
	}

	private Sound? GetOwnerSound()
	{
		if (_baseAsset.LinkedTo.Count > 0 && _baseAsset.LinkedTo[0] is Sound sound)
			return sound;
		return null;
	}

	private void StartPreview(AudioStream stream)
	{
		_player?.QueueFree();

		float startPos = (float)_timeSlider.Value;
		float length = (float)stream.GetLength();

		_ownerSound = GetOwnerSound();

		_player = new AudioStreamPlayer
		{
			Stream = stream,
			VolumeDb = Mathf.LinearToDb(_ownerSound?.Volume ?? 1f),
			PitchScale = _ownerSound?.Pitch ?? 1f
		};
		AddChild(_player);
		_player.Finished += OnFinished;

		_player.Play();

		if (startPos > 0.01f && startPos < length)
		{
			_player.Seek(startPos);
		}

		_isPlaying = true;
		_previewButton.Icon = _pauseIcon;
		SetProcess(true);
	}

	private void StopPreview()
	{
		if (_player != null)
		{
			_player.Finished -= OnFinished;
			_player.Stop();
			_player.QueueFree();
			_player = null;
		}
		_isPlaying = false;
		_previewButton.Icon = _playIcon;
		SetProcess(false);
	}

	private void OnFinished()
	{
		if (_player == null) return;

		if (_ownerSound != null && _ownerSound.Loop)
		{
			_player.Play();
			_timeSlider.Value = 0;
		}
		else
		{
			_player.Finished -= OnFinished;
			_player.QueueFree();
			_player = null;
			_isPlaying = false;
			_previewButton.Icon = _playIcon;
			_timeSlider.Value = 0;
			SetProcess(false);
		}
	}

	private void OnTimeChanged(double value)
	{
		if (_player != null)
		{
			_player?.Seek((float)value);
		}
	}

	private void OnResourceLoaded(Resource resource)
	{
		var stream = (AudioStream)resource;

		if (_isPlaying)
			StopPreview();

		_timeSlider.Value = 0;
		_timeSlider.MaxValue = (float)stream.GetLength();
		_timeSlider.Editable = true;
		_timeSlider.RemoveThemeStyleboxOverride("slider");
		_previewButton.Disabled = false;
		_previewButton.Icon = _playIcon;
	}

	public override void _ExitTree()
	{
		SetProcess(false);
		if (_player != null)
		{
			_player.Finished -= OnFinished;
			_player.QueueFree();
			_player = null;
		}
		if (_baseAsset != null)
		{
			_baseAsset?.ResourceLoaded -= OnResourceLoaded;
		}
	}
}
