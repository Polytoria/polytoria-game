// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Linq;

namespace Polytoria.Mobile.UI;

public partial class ViewHomePage : MobileViewBase
{
	private const int RowLength = 10;
	private const float ParallaxFactor = 0.5f;
	private const float GreetingSlide = 16f;

	[Export] private ScrollContainer _scroll = null!;
	[Export] private Control _heroBackground = null!;
	[Export] private Control _greeting = null!;
	[Export] private Label _usernameLabel = null!;
	[Export] private AvatarPreview _avatar = null!;
	[Export] private WorldRow _continueRow = null!;
	[Export] private WorldRow _liveNowRow = null!;
	[Export] private WorldRow _recommendedRow = null!;

	private Vector2 _greetingPosition;
	private bool _worldsLoaded;
	private bool _loadingWorlds;
	private Tween? _greetingTween;

	public override void _EnterTree()
	{
		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		PolyMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		base._ExitTree();
	}

	public override void _Ready()
	{
		_greetingPosition = _greeting.Position;
		_scroll.GetVScrollBar().ValueChanged += OnScrolled;

		if (PolyMobileAuthAPI.CurrentUserInfo.Id != 0)
		{
			LoadUser(PolyMobileAuthAPI.CurrentUserInfo);
		}
	}

	public override void ShowView(object? args)
	{
		_avatar.Wave();
		PlayGreeting();

		if (!_worldsLoaded)
		{
			LoadWorlds();
		}

		base.ShowView(args);
	}

	private void OnUserAuthenticated(APIMeResponse me)
	{
		LoadUser(me);
	}

	private void LoadUser(APIMeResponse me)
	{
		_usernameLabel.Text = me.Username + "!";
		_avatar.LoadUser(me.Id);
	}

	private async void LoadWorlds()
	{
		if (_loadingWorlds)
		{
			return;
		}

		_loadingWorlds = true;
		_continueRow.ShowLoading();
		_liveNowRow.ShowLoading();
		_recommendedRow.ShowLoading();

		try
		{
			APIWorldsData[] worlds = [.. (await WorldsCache.GetWorlds()).Data.Where(w => !w.IsLegacy)];

			_continueRow.SetWorlds(worlds.Take(RowLength));
			_liveNowRow.SetWorlds(worlds.Where(w => w.Playing > 0).OrderByDescending(w => w.Playing).Take(RowLength));
			_recommendedRow.SetWorlds(worlds.OrderByDescending(w => w.Rating ?? 0).Take(RowLength));
			_worldsLoaded = true;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			_continueRow.SetWorlds([]);
			_liveNowRow.SetWorlds([]);
			_recommendedRow.SetWorlds([]);
		}

		_loadingWorlds = false;
	}

	private void PlayGreeting()
	{
		_greetingTween?.Kill();
		_greeting.Modulate = new(1, 1, 1, 0);
		_greeting.Position = _greetingPosition - new Vector2(GreetingSlide, 0);

		_greetingTween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		_greetingTween.TweenProperty(_greeting, "modulate:a", 1f, 0.4f).SetDelay(0.1f);
		_greetingTween.TweenProperty(_greeting, "position", _greetingPosition, 0.5f).SetDelay(0.1f);
	}

	private void OnScrolled(double value)
	{
		_heroBackground.Position = new(0, (float)value * ParallaxFactor);
	}
}
