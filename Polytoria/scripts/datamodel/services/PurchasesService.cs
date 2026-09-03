// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Client.UI.Purchases;
using Polytoria.Client.WebAPI;
using Polytoria.Networking;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Purchases"), ExplorerExclude, SaveIgnore]
public sealed partial class PurchasesService : Instance
{
	private readonly PTHttpClient _client = new();
	private readonly Dictionary<string, PurchaseRequest> _pendingPurchases = [];
	private readonly HashSet<Player> _pendingPlayers = [];
	private string _currentPurchaseRef = "";
	private int _currentExpectedPrice = 0;
	private UIPurchasePrompt? _purchasePrompt;

	public override void Init()
	{
		if (Root.IsLoaded)
		{
			OnGameReady();
		}
		else
		{
			Root.Loaded.Once(OnGameReady);
		}

		SetProcess(true);
		base.Init();
	}

	private async void OnGameReady()
	{
		if (Root == null || Root.CoreUI == null) return;
		CoreUIRoot root = await Root.CoreUI.WaitRoot();
		_purchasePrompt = root.PurchasePrompt;
		_purchasePrompt?.Requested += OnPurchasePromptRequested;
	}

	public override void PreDelete()
	{
		_purchasePrompt?.Requested -= OnPurchasePromptRequested;
		base.PreDelete();
	}

	private void OnPurchasePromptRequested(bool accepted)
	{
		SendPurchaseRes(accepted);
	}

	public override void Process(double delta)
	{
		if (Root != null && Root.Network.IsServer)
		{
			CleanupExpiredPurchases();
		}
		base.Process(delta);
	}

	[ScriptMethod]
	public async Task<bool> PromptAsync(Player player, int assetID)
	{
		ServerGuard();
		if (_pendingPlayers.Contains(player)) return false;

		string refID = Guid.NewGuid().ToString();
		TaskCompletionSource<bool> tcs = new();

		_pendingPurchases[refID] = new()
		{
			Player = player,
			AssetID = assetID,
			TaskSource = tcs,
			Timestamp = DateTime.Now
		};

		_pendingPlayers.Add(player);

		RpcId(player.PeerID, nameof(NetRecvPurchase), assetID, refID);

		return await tcs.Task;
	}

	[ScriptLegacyMethod("Prompt")]
	public void LegacyPrompt(Player player, int assetID, PTCallback callback)
	{
		ServerGuard();
		PromptAsync(player, assetID).ContinueWith(task =>
		{
			if (task.IsCompletedSuccessfully)
			{
				callback.Invoke(task.Result, "Purchase processed");
			}
			else if (task.IsFaulted && task.Exception != null)
			{
				callback.Invoke(false, "Purchase processing failed");
			}
		});
	}

	[ScriptMethod]
	public async Task<bool> OwnsItemAsync(Player player, int assetID)
	{
		ServerGuard();
		using HttpResponseMessage res = await _client.GetAsync(Globals.ApiEndpoint.PathJoin($"/v1/store/{assetID}/owner?userID={player.UserID}"));
		res.EnsureSuccessStatusCode();

		APIOwnsItem item = await res.Content.ReadFromJsonAsync(APIGenerationContext.Default.APIOwnsItem);
		return item.Owned;
	}

	private void SendPurchaseRes(bool status)
	{
		RpcId(1, nameof(NetRecvPurchaseRes), _currentPurchaseRef, status, _currentExpectedPrice);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetRecvPurchase(int assetID, string refID)
	{
		_currentPurchaseRef = refID;

		PT.Print("Purchase initiated with ID ", assetID);

		try
		{
			APIStoreItem storeItem = await PolyAPI.GetStoreItem(assetID);
			if (!storeItem.Price.HasValue) throw new Exception("This item does not have a price");
			_currentExpectedPrice = storeItem.Price.Value;
			_purchasePrompt?.Prompt(storeItem);
		}
		catch (Exception ex)
		{
			PT.PrintErr("Purchase processing failure: ", ex.Message);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetRecvPurchaseRes(string refID, bool accepted, int expectedPrice)
	{
		int peerID = RemoteSenderId;
		Player? plr = Root.Players.GetPlayerFromPeerID(peerID);

		if (plr != null)
		{
			if (_pendingPurchases.TryGetValue(refID, out var request))
			{
				if (request.Player != plr)
				{
					// Player mismatch
					return;
				}
				_pendingPlayers.Remove(plr);
				if (accepted)
				{
					_pendingPurchases.Remove(refID, out PurchaseRequest req);
					req.ExpectedPrice = expectedPrice;
					RequestProcessPurchase(req);
				}
				else
				{
					// Purchase declined
					_pendingPurchases.Remove(refID);
					request.TaskSource.SetResult(false);
				}
			}
		}
	}

	private async void RequestProcessPurchase(PurchaseRequest req)
	{
		if (!Root.Network.IsProd || Root.IsLocalTest)
		{
			SendProcessSuccessful(req, true);
			return;
		}

		try
		{
			List<KeyValuePair<string, string>> formVariables =
			[
				new("assetID", req.AssetID.ToString()),
				new("userID", req.Player.UserID.ToString()),
				new("expectedPrice", req.ExpectedPrice.ToString()),
				new("timestamp", req.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ")),
			];
			FormUrlEncodedContent formContent = new(formVariables);

			_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;
			using var pa = await _client.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v1/game/server/purchase"),
				formContent
			);

			pa.EnsureSuccessStatusCode();
			APIPurchaseResponse purchaseRes = await pa.Content.ReadFromJsonAsync(ServerAPIGenerationContext.Default.APIPurchaseResponse);
			SendProcessSuccessful(req, purchaseRes.Success);
		}
		catch (Exception ex)
		{
			PT.PrintErr("Purchase processing failure: ", ex.Message);
			SendProcessSuccessful(req, false);
		}
	}

	private void SendProcessSuccessful(PurchaseRequest req, bool status)
	{
		req.TaskSource.SetResult(status);
		_pendingPlayers.Remove(req.Player);
		RpcId(req.Player.PeerID, nameof(NetRecvPurchaseProcessRes), _currentPurchaseRef, status);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvPurchaseProcessRes(string refID, bool success)
	{
		if (refID == _currentPurchaseRef)
		{
			_currentPurchaseRef = "";
		}

		if (success)
		{
			_purchasePrompt?.PlayPurchaseSuccess();
		}
		else
		{
			// TODO: Implement error modal
			_purchasePrompt?.Hide();
		}
	}

	private void CleanupExpiredPurchases()
	{
		if (_pendingPurchases.Count == 0) return;

		List<string> keysToRemove = [];
		DateTime expireTime = DateTime.Now.AddMinutes(-5);

		foreach (var kvp in _pendingPurchases)
		{
			if (kvp.Value.Timestamp < expireTime)
			{
				keysToRemove.Add(kvp.Key);
			}
		}

		foreach (var key in keysToRemove)
		{
			var request = _pendingPurchases[key];
			_pendingPurchases.Remove(key);
			_pendingPlayers.Remove(request.Player);

			request.TaskSource.SetException(new TimeoutException("Purchase request timed out."));
		}
	}

	public struct PurchaseRequest
	{
		public Player Player;
		public int AssetID;
		public int ExpectedPrice;
		public TaskCompletionSource<bool> TaskSource;
		public DateTime Timestamp;
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Purchases can only be accessed by server");
	}
}
