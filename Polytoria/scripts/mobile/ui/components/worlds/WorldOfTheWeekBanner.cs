// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Utils;
using System;
using System.Linq;

namespace Polytoria.Mobile.UI;

public partial class WorldOfTheWeekBanner : Button
{
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _content = null!;
	[Export] private Label _titleLabel = null!;
	[Export] private Label _descriptionLabel = null!;
	[Export] private BaseButton _playButton = null!;

	private const float PlayPressedScale = 0.9f;

	private int _placeID;
	private Tween? _playTween;

	public override void _Ready()
	{
		_content.Modulate = new(1, 1, 1, 0);
		_playButton.Modulate = new(1, 1, 1, 0);
		_playButton.Disabled = true;

		Pressed += OnPressed;

		_playButton.ButtonDown += () => _playTween = CardAnimator.Press(_playButton, _playTween, true, PlayPressedScale);
		_playButton.ButtonUp += () => _playTween = CardAnimator.Press(_playButton, _playTween, false);
		_playButton.Pressed += OnPlayPressed;

		LoadWorldOfTheWeek();
	}

	private async void LoadWorldOfTheWeek()
	{
		try
		{
			APIWorldOfTheWeek wotw = (await PolyAPI.GetWorldOfTheWeek()).WorldOfTheWeek;
			APIPlaceInfo place = wotw.Place;

			_placeID = place.Id;
			_titleLabel.Text = place.Name;

			string description = string.IsNullOrWhiteSpace(wotw.Description) ? place.Description : wotw.Description;
			_descriptionLabel.Text = description.Replace("\r\n", " ").Replace('\n', ' ');

			LoadImage(wotw.ImageUrl, LoadFallbackImage);
			ShowContent();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			Visible = false;
		}
	}

	private void LoadImage(string? url, Action? failed = null)
	{
		if (string.IsNullOrEmpty(url))
		{
			failed?.Invoke();
			return;
		}

		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, (resource) =>
		{
			_thumbnailRect.Texture = (Texture2D)resource;
		}, failed);
	}

	private async void LoadFallbackImage()
	{
		try
		{
			APIPlaceMedia[]? media = await PolyAPI.GetWorldMedia(_placeID);
			LoadImage(media?.FirstOrDefault(m => m.Type == "thumbnail").Url);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private void ShowContent()
	{
		_playButton.Disabled = false;

		Tween fade = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		fade.TweenProperty(_content, "modulate:a", 1f, 0.35f);
		fade.TweenProperty(_playButton, "modulate:a", 1f, 0.35f).SetDelay(0.15f);

		_playButton.Scale = new(0.7f, 0.7f);
		_playTween = CreateTween();
		_playTween.TweenProperty(_playButton, "scale", Vector2.One, 0.5f)
			.SetDelay(0.15f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
	}

	private void OnPressed()
	{
		if (_placeID != 0)
		{
			MobileUI.Singleton.SwitchTo(MobileViewEnum.PlaceInfo, _placeID);
		}
	}

	private void OnPlayPressed()
	{
		MobileUI.Singleton.LaunchGame(_placeID);
	}
}
