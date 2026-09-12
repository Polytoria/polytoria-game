// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Discord;
using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Shared;
using System;

namespace Polytoria.Datamodel.Services;

[Static("Presence"), ExplorerExclude, SaveIgnore]
public sealed partial class PresenceService : Instance
{
	private const long DiscordAppID = 715468601540476959;
	private string? _state;
	private PTImageAsset? _coverImage;
	private ActivityManager? _activityManager;
	private Discord.Discord? _discord;
	private bool _updateDirty = false;
	private string? _imageURL;
	private static bool _creatorActivityStarted = false;

	private long _startTime = 0;

	[ScriptProperty, SyncVar]
	public string? State
	{
		get => _state;
		set
		{
			_state = value;
			QueueUpdatePresence();
			OnPropertyChanged();
		}
	}

	[ScriptProperty, SyncVar]
	public PTImageAsset? CoverImage
	{
		get => _coverImage;
		set
		{
			if (_coverImage != null && _coverImage != value)
			{
				_coverImage.ResourceLoaded -= OnCoverImageLoaded;
				_coverImage.UnlinkFrom(this);
			}
			_coverImage = value;

			if (_coverImage != null)
			{
				_coverImage.ResourceLoaded += OnCoverImageLoaded;
				_coverImage.LinkTo(this);

				if (_coverImage.IsResourceLoaded && _coverImage.Resource != null)
				{
					OnCoverImageLoaded(_coverImage.Resource);
				}
				else
				{
					_coverImage.QueueLoadResource();
				}
			}

			QueueUpdatePresence();
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		Globals.BeforeQuit += BeforeQuit;
		SetProcess(true);
		base.Init();
	}

	public override void PreDelete()
	{
		Globals.BeforeQuit -= BeforeQuit;
		base.PreDelete();
	}

	public void BeforeQuit()
	{
		DisposeDiscord();
	}

	public override void Ready()
	{
		base.Ready();
		SetupIntegrations();
		ResetTimer();

		Root.Players.PlayerAdded.Connect((_) => { QueueUpdatePresence(); });
		Root.Players.PlayerRemoved.Connect((_) => { QueueUpdatePresence(); });
	}

	private void OnCoverImageLoaded(Resource _)
	{
		_imageURL = CoverImage?.DirectImageURL ?? null;
		UpdateIntegrations();
	}

	[ScriptMethod]
	public void ResetTimer()
	{
		_startTime = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
	}

	public override void Process(double delta)
	{
		if (!Root.IsLoaded) return;
		if (_updateDirty)
		{
			_updateDirty = false;
			UpdateIntegrations();
		}
		DiscordTick();
		base.Process(delta);
	}

	private void SetupIntegrations()
	{
		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			// TODO: We need a separate global for managing creator presence
			if (_creatorActivityStarted) return;
			_creatorActivityStarted = true;
		}
		try
		{
			SetupDiscord();
			UpdateIntegrations();
		}
		catch
		{
			// ignore the error its lowk annoying
		}
	}

	private void QueueUpdatePresence()
	{
		_updateDirty = true;
	}

	private void UpdateIntegrations()
	{
		try
		{
			UpdateDiscord();
		}
		catch { }
	}

	private void SetupDiscord()
	{
		if (!OS.HasFeature("discord-rpc")) return;
		_discord = new(DiscordAppID, (ulong)CreateFlags.NoRequireDiscord);
		_activityManager = _discord.GetActivityManager();
	}

	private void DisposeDiscord()
	{
		_discord = null;
	}

	private void UpdateDiscord()
	{
		if (_activityManager == null) return;

		string details;
		string largeText = "Testing...";

		if (Root.WorldInfo.HasValue)
		{
			details = $"Playing {Root.WorldInfo.Value.Name}";
			largeText = Root.WorldInfo.Value.Name;
		}
		else
		{
			details = "Testing a game";
		}

		string defaultImg = Root.WorldInfo?.Thumbnail ?? "multiplayer";
		string defaultSmallImg = "poly-sm";
		string defaultSmallText = "Polytoria";

		if (Root.SessionType == World.SessionTypeEnum.Creator)
		{
			defaultImg = "creating";
			defaultSmallImg = "creator-sm";
			defaultSmallText = "Polytoria Creator";
			largeText = "Tinkering";
			details = "Creating world";
		}

		Discord.Activity activity = new()
		{
			State = _state != null ? FilterService.Filter(_state) : "",
			Details = details,
			Timestamps =
			{
				Start = _startTime,
			},
			Assets =
			{
				LargeImage = _imageURL ?? defaultImg,
				LargeText = largeText,
				SmallImage = defaultSmallImg,
				SmallText = defaultSmallText,
			},
			Party =
			{
				Id = Guid.NewGuid().ToString(),
				Size =
				{
					CurrentSize = Root.Players.PlayersCount,
					MaxSize = Root.WorldInfo?.MaxPlayers ?? 8,
				},
			},
			Instance = true
		};

		_activityManager.UpdateActivity(activity, (result) =>
		{
		});
	}

	private void DiscordTick()
	{
		if (_discord == null) return;
		try
		{
			_discord.RunCallbacks();
		}
		catch { }
	}
}
