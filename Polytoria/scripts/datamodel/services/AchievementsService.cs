// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Providers.Achievements;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Achievements")]
public sealed partial class AchievementsService : Instance
{
	private const int MaxRequestsPerMinute = 30;
	private const int RequestsPerPlayerModifier = 10;

	private IAchievementsProvider _provider = null!;

	private bool _useAchievementSound = true;
	private bool _notifyAchievements = true;
	private readonly HashSet<int> _gotAchievements = [];
	private int _requestsThisMinute = 0;
	private int _currentMinute = 0;

	public override void Init()
	{
		_provider = Root.Network.IsProd ? new PTAchievementsProvider() : new LocalAchievementsProvider();
		base.Init();
	}

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

		bool hasPrev = await _provider.RequestHasAchievement(userID, achievementID);
		await _provider.RequestGiveAchievement(userID, achievementID);

		if (targetPlr != null && !hasPrev)
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

		return await _provider.RequestHasAchievement(userID, achievementID);
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Achievements can only be accessed by server");
	}
}
