// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Services;
using Polytoria.Networking;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Datamodel;

[Static("Players")]
public sealed partial class Players : Instance
{
	private bool _playerCollisionEnabled = true;
	private bool _useServerAuthority = true;

	[ScriptProperty]
	public Player LocalPlayer { get; private set; } = null!;

	[Editable, ScriptProperty]
	public bool PlayerCollisionEnabled
	{
		get => _playerCollisionEnabled;
		set
		{
			_playerCollisionEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable]
	public bool UseServerAuthority
	{
		get => _useServerAuthority;
		set
		{
			_useServerAuthority = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public PTSignal<Player> PlayerAdded { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<Player> PlayerRemoved { get; private set; } = new();

	[ScriptProperty]
	public int PlayersCount => GetChildren().Count(c => c is Player plr && plr.IsReady);

	/// <summary>
	/// Get current player count including connecting
	/// </summary>
	public int AbsolutePlayersCount => GetChildren().Count(c => c is Player plr);

	internal readonly Dictionary<int, Player> PeerIDToPlayer = [];
	public readonly Dictionary<int, Player> _idToPlayer = [];

	/// <summary>
	/// Get Player from Peer ID. This should only be called on server, as peer is not guaranteed to be available on clients.
	/// </summary>
	/// <param name="peerID"></param>
	/// <returns></returns>
	internal Player? GetPlayerFromPeerID(int peerID)
	{
		if (PeerIDToPlayer.TryGetValue(peerID, out Player? player)) return player;
		return null;
	}

	internal int[] GetPlayerIDArray()
	{
		List<int> ids = [];

		foreach (Instance n in GetChildren())
		{
			if (n is Player plr)
			{
				ids.Add(plr.UserID);
			}
		}

		return [.. ids];
	}

	[ScriptMethod]
	public Player[] GetPlayers()
	{
		List<Player> plrs = [];

		foreach (Instance n in GetChildren())
		{
			if (n is Player plr && plr.IsReady)
			{
				plrs.Add(plr);
			}
		}

		return [.. plrs];
	}

	[ScriptMethod]
	public Player? GetPlayer(string username)
	{
		return FindChild<Player>(username);
	}

	[ScriptMethod]
	public Player? GetPlayerByID(int userID)
	{
		if (_idToPlayer.TryGetValue(userID, out Player? player)) return player;

		// fallback: Find from each players
		foreach (Player plr in GetPlayers())
		{
			if (plr.UserID == userID)
			{
				// cache the result
				_idToPlayer[userID] = plr;
				return plr;
			}
		}
		return null;
	}

	internal void InvokePlayerAdded(Player player)
	{
		Rpc(nameof(OnPlayerAdded), player.Name);
	}

	internal void InvokePlayerRemoved(Player player)
	{
		Rpc(nameof(OnPlayerRemoved), player.Name);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void OnPlayerAdded(string username)
	{
		Player? plr = GetPlayer(username);

		if (plr != null)
		{
			PeerIDToPlayer.TryAdd(plr.PeerID, plr);
			_idToPlayer.Add(plr.UserID, plr);
			PlayerAdded.Invoke(plr);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void OnPlayerRemoved(string username)
	{
		Player? plr = GetPlayer(username);

		if (plr != null)
		{
			PeerIDToPlayer.Remove(plr.PeerID);
			_idToPlayer.Remove(plr.UserID);
			PlayerRemoved.Invoke(plr);
		}
	}

	// Request Localplayer from the server
	internal void ReqLocalPlayer()
	{
		RpcId(1, nameof(NetReqLocalPlayer));
	}

	[NetRpc(AuthorityMode.Any, CallLocal = false, TransferMode = TransferMode.Reliable)]
	private void NetReqLocalPlayer()
	{
		int peerID = RemoteSenderId;
		Player? plr = GetPlayerFromPeerID(peerID);
		if (plr != null)
		{
			RpcId(peerID, nameof(NetRecvLocalPlayer), plr.Name);
		}
		else
		{
			Root.Network.DisconnectPeer(peerID, "INTERNAL BUG: Player not found on server, please rejoin", NetworkService.DisconnectionCodeEnum.PlayerNotFound);
		}
	}

	[NetRpc(AuthorityMode.Server, CallLocal = false, TransferMode = TransferMode.Reliable)]
	private void NetRecvLocalPlayer(string plrName)
	{
		LocalPlayer = FindChild<Player>(plrName)!;
		if (LocalPlayer == null)
		{
			Root.Network.DisconnectSelf("INTERNAL BUG: Player not found, please rejoin", NetworkService.DisconnectionCodeEnum.PlayerNotFound);
			return;
		}
		LocalPlayer.OnNetReady();
		Root.Network.OnLocalPlayerReady();
	}

	internal void AdminKick(string username)
	{
		if (GetPlayer(username) is Player plr)
		{
			plr.AdminKick();
		}
	}
}
