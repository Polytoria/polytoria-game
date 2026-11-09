// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	[Export] private Button _playButton = null!;
	[Export] private Button _backButton = null!;
	[Export] private ScrollContainer _scroll = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _genreChip = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _creatorNameLabel = null!;
	[Export] private Label _playingLabel = null!;
	[Export] private Label _visitsLabel = null!;
	[Export] private Label _ratingLabel = null!;
	[Export] private Label _descriptionLabel = null!;

	private const float PlayPressedScale = 0.96f;

	private Texture2D _placeholderThumbnail = null!;
	private int _worldID;
	private int _loadToken;
	private bool _loading;
	private Tween? _playTween;

	public override void _Ready()
	{
		_placeholderThumbnail = _thumbnailRect.Texture;

		_playButton.Pressed += OnPlayButtonPressed;
		_playButton.ButtonDown += () => _playTween = CardAnimator.Press(_playButton, _playTween, true, PlayPressedScale);
		_playButton.ButtonUp += () => _playTween = CardAnimator.Press(_playButton, _playTween, false);
		_backButton.Pressed += MobileUI.Singleton.GoBack;
	}

	private void OnPlayButtonPressed()
	{
		MobileUI.Singleton.LaunchGame(_worldID);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);
		_worldID = (int)args!;
		int token = ++_loadToken;

		_scroll.ScrollVertical = 0;
		_thumbnailRect.Texture = _placeholderThumbnail;
		_genreChip.Visible = false;
		_placeNameLabel.Text = "";
		_creatorNameLabel.Text = "";
		_playingLabel.Text = "--";
		_visitsLabel.Text = "--";
		_ratingLabel.Text = "--";
		_descriptionLabel.Text = "";

		SetLoading(true);

		try
		{
			APIPlaceInfo placeInfo = await PolyAPI.GetWorldFromID(_worldID);

			if (token != _loadToken)
			{
				return;
			}

			_genreChip.Visible = !string.IsNullOrEmpty(placeInfo.Genre);
			_genreLabel.Text = placeInfo.Genre;
			_placeNameLabel.Text = placeInfo.Name;
			_creatorNameLabel.Text = "By " + placeInfo.Creator.Name;
			_playingLabel.Text = FormatCount(placeInfo.Playing) + " playing";
			_visitsLabel.Text = FormatCount(placeInfo.Visits) + " visits";
			_ratingLabel.Text = FormatRating(placeInfo.Rating);
			_descriptionLabel.Text = placeInfo.Description;

			LoadThumbnail(placeInfo.Thumbnail, token);
		}
		catch (Exception ex)
		{
			if (token != _loadToken)
			{
				return;
			}

			PT.PrintErr(ex);
		}

		SetLoading(false);
	}

	public override void HideView()
	{
		_loadToken++;
		SetLoading(false);
		base.HideView();
	}

	private void SetLoading(bool loading)
	{
		if (loading == _loading)
		{
			return;
		}

		_loading = loading;
		if (loading)
		{
			MobileUI.Singleton.LoadingScreen.ShowScreen();
		}
		else
		{
			MobileUI.Singleton.LoadingScreen.HideScreen();
		}
	}

	private void LoadThumbnail(string url, int token)
	{
		if (string.IsNullOrEmpty(url))
		{
			return;
		}

		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, (resource) =>
		{
			if (token == _loadToken)
			{
				_thumbnailRect.Texture = (Texture2D)resource;
			}
		});
	}

	private static string FormatRating(APIPlaceRating rating)
	{
		int total = rating.Likes + rating.Dislikes;
		if (total == 0)
		{
			return "--";
		}

		return Mathf.RoundToInt(rating.Likes * 100f / total) + "%";
	}

	private static string FormatCount(int count)
	{
		return count switch
		{
			>= 1_000_000 => (count / 1_000_000f).ToString("0.#") + "M",
			>= 1_000 => (count / 1_000f).ToString("0.#") + "K",
			_ => count.ToString()
		};
	}
}
