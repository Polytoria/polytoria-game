// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.WebAPI;
using Polytoria.Networking;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Achievements")]
public sealed partial class AchievementsService : Instance
{
	private const int MaxRequestsPerMinute = 30;
	private const int RequestsPerPlayerModifier = 10;

	private readonly PTHttpClient _client = new();

	private bool _useAchievementSound = true;
	private bool _notifyAchievements = true;
	private readonly HashSet<int> _gotAchievements = [];
	private int _requestsThisMinute = 0;
	private int _currentMinute = 0;

	[ScriptProperty] public PTSignal<int> GotAchievement { get; private set; } = new();

	[Editable, ScriptProperty]
	public bool UseAchievementSound
	{
		get => _useAchievementSound;
		set
		{
			_useAchievementSound = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool NotifyAchievements
	{
		get => _notifyAchievements;
		set
		{
			_notifyAchievements = value;
			OnPropertyChanged();
		}
	}

	private bool UseRequest()
	{
		if (_currentMinute != DateTime.Now.Minute)
		{
			_currentMinute = DateTime.Now.Minute;
			_requestsThisMinute = 0;
		}

		if (_requestsThisMinute >= MaxRequestsPerMinute + (RequestsPerPlayerModifier * Root.Players.PlayersCount))
		{
			return false;
		}
		else
		{
			_requestsThisMinute++;
			return true;
		}
	}

	[ScriptMethod, ScriptLegacyMethod("Award")]
	public void Award(int userID, int achievementID, PTCallback? callback)
	{
		_ = AwardAsync(userID, achievementID).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				callback?.Invoke(true);
			}
			else
			{
				callback?.Invoke(false, tsk.Exception?.Message);
			}
		});
	}

	[ScriptMethod]
	public async Task AwardAsync(int userID, int achievementID)
	{
		ServerGuard();

		Player? targetPlr = Root.Players.GetPlayerByID(userID);


		if (!UseRequest())
		{
			throw new Exception("Request limit exceeded, please try again later.");
		}

		bool hasPrev = false;

		if (Root.Network.IsProd)
		{
			hasPrev = await RequestHasAchievement(userID, achievementID);
			await RequestGiveAchievement(userID, achievementID);
		}

		if (targetPlr != null && (!hasPrev || Root.IsLocalTest))
		{
			RpcId(targetPlr.PeerID, nameof(NetRecvAchievement), achievementID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvAchievement(int id)
	{
		if (_gotAchievements.Contains(id)) return;
		_gotAchievements.Add(id);
		GotAchievement.Invoke(id);
	}

	[ScriptMethod, ScriptLegacyMethod("HasAchievement")]
	public void HasAchievement(int userID, int achievementID, PTCallback callback)
	{
		_ = HasAchievementAsync(userID, achievementID).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				bool hasA = tsk.Result;
				callback.Invoke(hasA, true);
			}
			else
			{
				callback.Invoke(false, false, tsk.Exception?.Message);
			}
		});
	}

	[ScriptMethod]
	public async Task<bool> HasAchievementAsync(int userID, int achievementID)
	{
		ServerGuard();

		if (!UseRequest())
		{
			throw new Exception("Request limit exceeded, please try again later.");
		}

		if (Root.Network.IsProd)
		{
			return await RequestHasAchievement(userID, achievementID);
		}

		return false;
	}

	internal async Task RequestGiveAchievement(int userID, int achievementID)
	{
		SetHttpClientAuthToken();

		List<KeyValuePair<string, string>> formVariables =
		[
			new("userID", userID.ToString()),
			new("achievementID", achievementID.ToString()),
		];
		FormUrlEncodedContent formContent = new(formVariables);

		using var pa = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v1/game/server/achievements/award"),
			formContent
		);
	}

	internal async Task<bool> RequestHasAchievement(int userID, int achievementID)
	{
		SetHttpClientAuthToken();
		APIHasAchievementResponse res = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/game/server/achievements/has-achievement?userID=" + userID + "&achievementID=" + achievementID),
			ServerAPIGenerationContext.Default.APIHasAchievementResponse
		);
		return res.HasAchievement;
	}

	private void SetHttpClientAuthToken()
	{
		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Achievements can only be accessed by server");
	}
}
