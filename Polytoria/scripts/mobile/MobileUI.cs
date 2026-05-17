// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DeepLinkAddon;
using Godot;
using Polytoria.Client;
using Polytoria.Mobile.UI;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;

namespace Polytoria.Mobile;

public partial class MobileUI : Control
{
	public static MobileUI Singleton { get; private set; } = null!;
	public MobileUI()
	{
		Singleton = this;
	}

	public event Action<MobileViewEnum>? ViewPathSwitched;

	private Control _mainView = null!;
	public MobileViewBase? CurrentViewNode;
	public MobileViewEnum CurrentView;

	[Export] public StartupSplash? StartSplash { get; private set; }
	[Export] public NewUserSplash NewUserSplash = null!;
	[Export] public MobileLoadingScreen LoadingScreen = null!;

	private Deeplink _deepLink = new();
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = [];

	public override void _Ready()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		AddChild(_deepLink, true);

		if (Globals.IsMobileBuild)
		{
			GetTree().Root.ContentScaleFactor = Globals.MobileScale;
		}

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		if (StartSplash != null)
		{
			StartSplash!.Visible = true;
		}

		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		PolyMobileAuthAPI.AskForAuthentication += OnAskForAuthentication;

		PolyMobileAuthAPI.SetupClient();
		if (mobileToken != null)
		{
			_ = PolyMobileAuthAPI.LoginWithAuthToken(mobileToken);
		}

		if (mobileCode != null && mobileState != null)
		{
			_ = PolyMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState);
		}

		_deepLink.DeeplinkReceived += OnDeeplinkReceived;

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
		}

		SwitchTo(MobileViewEnum.Home);
	}

	private void OnUserAuthenticated(APIMeResponse me)
	{
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
	}

	private void OnAskForAuthentication()
	{
		HideStartupSplash();
		if (!Globals.IsInGDEditor)
		{
			NewUserSplash.ShowSplash();
		}
	}

	private void HideStartupSplash()
	{
		StartSplash?.HideSplash();
		StartSplash = null;
	}

	private async void OnDeeplinkReceived(DeeplinkURL url)
	{
		// Handle polytoria://auth link
		if (url.Host == "auth")
		{
			NameValueCollection authQuery = HttpUtility.ParseQueryString(url.Query);
			string code = authQuery.Get("code")!;
			string state = authQuery.Get("state")!;

			LoadingScreen.ShowScreen();
			await PolyMobileAuthAPI.LoginWithCodeAndState(code, state);
			LoadingScreen.HideScreen();
		}

		if (url.Host == "client")
		{
			PT.Print(url);
		}
	}

	public async void LaunchGame(int placeID)
	{
		LoadingScreen.ShowScreen();

		try
		{
			APIJoinPlaceResponse res = await PolyAPI.RequestJoinGame(new() { PlaceID = placeID, IsBeta = true });

			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				ClientEntry.ClientEntryData entryData = new()
				{
					Token = res.Token
				};
				ce.Entry(entryData);
			}
		}
		catch (Exception ex)
		{
			OS.Alert(ex.Message, "World join failed");
		}

		LoadingScreen.HideScreen();
	}

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		if (viewEnum == CurrentView)
		{
			return;
		}

		if (CurrentViewNode != null)
		{
			CurrentViewNode.HideView();
			CurrentViewNode.Visible = false;
		}

		// Check if cached
		if (!_viewCache.TryGetValue(viewEnum, out MobileViewBase? page))
		{
			PT.Print("Loading ", viewEnum);
			string pathToLoad = viewEnum switch
			{
				MobileViewEnum.Home => "res://scenes/mobile/views/home.tscn",
				MobileViewEnum.Worlds => "res://scenes/mobile/views/worlds.tscn",
				MobileViewEnum.PlaceInfo => "res://scenes/mobile/views/place_info.tscn",
				MobileViewEnum.Avatar => "res://scenes/mobile/views/avatar.tscn",
				MobileViewEnum.Dev => "res://scenes/mobile/views/test.tscn",
				_ => throw new ArgumentOutOfRangeException(nameof(viewEnum),
					 $"No scene defined for {viewEnum}")
			};

			PT.Print("Loading ", viewEnum);

			PackedScene packed = ResourceLoader.Load<PackedScene>(pathToLoad, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
			page = packed.Instantiate<MobileViewBase>();
			_viewCache[viewEnum] = page;
			_mainView.AddChild(page);
			page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		CurrentViewNode = page;
		page.ShowView(args);
		page.Visible = true;
		ViewPathSwitched?.Invoke(viewEnum);
	}
}

public enum MobileViewEnum
{
	None,
	Home,
	Worlds,
	Avatar,
	Store,
	Dev,
	PlaceInfo
}
