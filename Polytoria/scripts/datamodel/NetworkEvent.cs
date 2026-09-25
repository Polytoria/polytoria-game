// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Networking;
using Polytoria.Scripting;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class NetworkEvent : Instance
{
	private bool _reliable;

	/// <summary>
	/// Fires when the server receives a message from a client.
	/// </summary>
	[ScriptProperty] public PTSignal<Player, NetMessage> InvokedServer { get; private set; } = new();
	/// <summary>
	/// Fires when the client receives a message from the server.
	/// </summary>
	[ScriptProperty] public PTSignal<NetMessage> InvokedClient { get; private set; } = new();
	/// <summary>
	/// Fires when the client receives a message from the server on clients, or when the server receives a message from a client on the server.
	/// </summary>
	[ScriptProperty] public PTSignal<NetMessage, Player?> Invoked { get; private set; } = new();

	/// <summary>
	/// Fires when the client receives a message from the server.
	/// </summary>
	[ScriptLegacyProperty("InvokedClient")] public PTSignal LegacyInvokedClient { get; private set; } = new();

	/// <summary>
	/// Determine whether this network event should send messages reliably. It's recommended to enable this option when sending a large number of messages.
	/// </summary>
	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Reliable
	{
		get => _reliable;
		set
		{
			_reliable = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Sends a network event to the server from the client.
	/// </summary>
	/// <param name="msg"></param>
	[ScriptMethod]
	public void InvokeServer(NetMessage? msg = null, object? _ = null)
	{
		if (Root.Network.IsServer) throw new System.InvalidOperationException("InvokeServer can only be called from client");
		msg ??= new();

		if (Reliable)
		{
			RpcId(1, nameof(NetServerRecvMsg), msg.Serialize());
		}
		else
		{
			RpcId(1, nameof(NetServerRecvMsgUnreliable), msg.Serialize());
		}
	}

	/// <summary>
	/// Sends a network event to a specific player from the server
	/// </summary>
	/// <param name="player">player</param>
	/// <param name="msg">message</param>
	/// <exception cref="System.InvalidOperationException"></exception>
	[ScriptMethod]
	public void InvokeClient(Player? player = null, NetMessage? msg = null)
	{
		if (!Root.Network.IsServer) throw new System.InvalidOperationException("InvokeClient can only be called from server");
		ArgumentNullException.ThrowIfNull(player);
		msg ??= new();

		if (Reliable)
		{
			RpcId(player.PeerID, nameof(NetClientRecvMsg), msg.Serialize());
		}
		else
		{
			RpcId(player.PeerID, nameof(NetClientRecvMsgUnreliable), msg.Serialize());
		}
	}

	/// <summary>
	/// Sends a network event to a specific player from the server
	/// </summary>
	/// <param name="msg">message</param>
	/// <param name="player">player</param>
	/// <exception cref="System.InvalidOperationException"></exception>
	[ScriptMethod]
	public void InvokeClient(NetMessage? msg = null, Player? player = null)
	{
		InvokeClient(player, msg);
	}

	/// <summary>
	/// Sends a network event to all players from the server.
	/// </summary>
	/// <param name="msg">NetMessage to send</param>
	/// <exception cref="System.InvalidOperationException"></exception>
	[ScriptMethod]
	public void InvokeClients(NetMessage? msg = null)
	{
		if (!Root.Network.IsServer) throw new System.InvalidOperationException("InvokeClients can only be called from server");
		msg ??= new();

		if (Reliable)
		{
			Rpc(nameof(NetClientRecvMsg), msg.Serialize());
		}
		else
		{
			Rpc(nameof(NetClientRecvMsgUnreliable), msg.Serialize());
		}
	}

	/// <summary>
	/// Sends a network event to the server on clients, or on the server, sends a network event event to a player if the player is specified, or all players otherwise.
	/// </summary>
	/// <param name="msg">NetMessage to send</param>
	/// <param name="player">Target player. Only valid on the server. Sends to all players if omitted.</param>
	[ScriptMethod]
	public void Invoke(NetMessage? msg = null, Player? player = null)
	{
		if (Root.Network.IsServer)
		{
			if (player == null)
			{
				InvokeClients(msg);
			}
			else
			{
				InvokeClient(player, msg);
			}
		}
		else
		{
			InvokeServer(msg);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetClientRecvMsg(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetClientRecvMsgUnreliable(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetServerRecvMsg(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetServerRecvMsgUnreliable(byte[] rawdata)
	{
		RecvMsg(rawdata, RemoteSenderId);
	}

	private async void RecvMsg(byte[] rawdata, int sentBy)
	{
		try
		{
			NetMessage msg = await NetMessage.Deserialize(rawdata);

			if (Root.Network.IsServer)
			{
				Player? plr = Root.Players.GetPlayerFromPeerID(sentBy);
				if (plr != null)
				{
					InvokedServer.Invoke(plr, msg);
					Invoked.Invoke(msg, plr);
				}
			}
			else
			{
				LegacyInvokedClient.Invoke(null, msg);
				InvokedClient.Invoke(msg);
				Invoked.Invoke(msg, null);
			}
		}
		catch (Exception e)
		{
			GD.PushError(e);
		}
	}
}
