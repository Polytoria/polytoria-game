// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Networking;

/// <summary>
/// ENet network instance
/// </summary>
public class NetworkInstance
{
	private const float SilenceTimeoutSeconds = 5.0f;
	private const int DataChannelAuthTimeoutMs = 10000;
	private const ENetConnection.CompressionMode CompressionMode = ENetConnection.CompressionMode.Zlib;
	private const int BandwidthInLimit = 0;
	private const int BandwidthOutLimit = 30 * 1024;
	private const int BandwidthPerPlayer = 200 * 1024; // 200 KB/s per player
	private long _lastMessageTicks = DateTime.UtcNow.Ticks;
	private readonly Dictionary<int, string> _dataServerTokens = [];

	private const int DefaultCapacity = 67;
	private const int DefaultPort = 21441;
	private const int MinimumTimeout = 5;

	private readonly ENetConnection _peer;

	private readonly ConcurrentQueue<Action> _actionQueue = new();
	internal readonly ConcurrentDictionary<int, ENetPacketPeer> IdToPeer = [];
	internal readonly ConcurrentDictionary<ENetPacketPeer, int> PeerToId = [];

	private readonly ConcurrentQueue<DeferredNetworkEvent> _mainThreadEventQueue = new();
	private int _mainThreadDrainScheduled = 0;

	public ICollection<int> PeerIds => IdToPeer.Keys;

	private int _peerCounter = 1;

	public event Action<int>? PeerConnected;
	public event Action<int>? PeerDisconnected;
	public event Action? ClientConnected;
	public event Action? ClientDisconnected;
	public event Action<NetInstanceErrorEnum>? ClientError;
	public event MessageReceivedHandler? MessageReceived;

	public bool IsSilence { get; private set; } = false;
	public bool IsServer { get; private set; } = false;
	private bool _shutdownd = false;

	public NetworkInstance()
	{
		_peer = new();
	}

	public void CreateServer(int port = DefaultPort, int maxChannels = 3)
	{
		Error e = _peer.CreateHostBound("*", port, DefaultCapacity, maxChannels);
		_peer.Compress(CompressionMode);

		if (e != Error.Ok)
		{
			PT.PrintErr("Couldn't create host: ", e);
		}

		IsServer = true;

		PostPeerCreate();
	}

	public async Task CreateClient(string address, int port, int maxChannels = 3)
	{
		Error e = _peer.CreateHost(DefaultCapacity, maxChannels);
		_peer.BandwidthLimit(BandwidthInLimit, BandwidthOutLimit);
		_peer.Compress(CompressionMode);

		if (e != Error.Ok)
		{
			PT.PrintErr("Couldn't create host: ", e);
			return;
		}

		_peer.ConnectToHost(address, port);

		PostPeerCreate();
	}

	/// <summary>
	/// Adapt server bandwidth to player count
	/// </summary>
	/// <param name="_">used to be player count</param>
	public void AdaptBandwidth(int _)
	{
		// TODO: TEMP FIX, unlimit out bandwidth
		_peer.BandwidthLimit(0, 0);
	}

	private void PostPeerCreate()
	{
		_ = Task.Run(NetworkLoop);
	}

	internal bool VerifyDataServerToken(int peerID, string token)
	{
		if (_dataServerTokens.TryGetValue(peerID, out var val))
		{
			if (val == token)
			{
				// DataServer Token success, remove the token too
				_dataServerTokens.Remove(peerID);
				return true;
			}
		}
		return false;
	}

	public ENetPacketPeer? GetPacketPeerFromId(int id)
	{
		if (IdToPeer.TryGetValue(id, out ENetPacketPeer? p))
		{
			return p;
		}
		return null;
	}

	public void SendMessage(int targetID, byte[] data, TransferMode transferMode, int transferChannel = 0)
	{
		_actionQueue.Enqueue(() =>
		{
			ENetPacketPeer? peer = GetPacketPeerFromId(targetID);
			if (peer == null)
			{
				GD.PushWarning(targetID, " doesn't exist");
				return;
			}
			Error err = peer.Send(transferChannel, data, (int)transferMode);
			if (err != Error.Ok)
			{
				GD.PushError("Send error: ", err);
			}
		});
	}

	public void DisconnectPeer(int targetID, bool force = false)
	{
		_actionQueue.Enqueue(() =>
		{
			ENetPacketPeer? peer = GetPacketPeerFromId(targetID);
			if (peer == null)
			{
				GD.PushWarning(targetID, " doesn't exist");
				return;
			}
			if (force)
			{
				peer.PeerDisconnectNow();
			}
			else
			{
				peer.PeerDisconnect();
			}
		});
	}

	public void Shutdown()
	{
		if (_shutdownd) return;
		_shutdownd = true;
		foreach ((_, ENetPacketPeer pk) in IdToPeer)
		{
			pk.PeerDisconnect();
		}
		_peer.Flush();
		_peer.Destroy();
	}

	public void BroadcastMessage(byte[] data, TransferMode transferMode, int transferChannel = 0, int[]? except = null)
	{
		_actionQueue.Enqueue(() =>
		{
			foreach ((int id, ENetPacketPeer? peer) in IdToPeer)
			{
				if (!peer.IsActive()) continue;
				if (except != null && except.Contains(id)) continue;
				peer?.Send(transferChannel, data, (int)transferMode);
			}
		});
	}

	private void NetworkLoop()
	{
		while (true)
		{
			if (_shutdownd) return;
			if (!GodotObject.IsInstanceValid(_peer)) return;
			try
			{
				ProcessActionQueue();
				ProcessNetwork();
				CheckSilence();
				_peer.Flush();
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
			}
		}
	}

	public double PopStatistic(ENetConnection.HostStatistic hs)
	{
		return _peer.PopStatistic(hs);
	}

	private void ProcessNetwork()
	{
		Godot.Collections.Array serviceData = _peer.Service(MinimumTimeout);
		while (true)
		{
			ENetConnection.EventType eventType = (ENetConnection.EventType)(int)serviceData[0];
			if (eventType == ENetConnection.EventType.None)
				break;
			ENetPacketPeer? fromPeer = (ENetPacketPeer?)serviceData[1];
			int peerID = 0;
			if (fromPeer != null)
			{
				if (PeerToId.TryGetValue(fromPeer, out int p))
				{
					peerID = p;
				}
			}

			if (eventType == ENetConnection.EventType.Connect)
			{
				if (fromPeer == null) { PT.PrintWarn("Connect received but peer is null, return"); return; }

				if (!IsServer)
				{
					peerID = 1;
				}
				else
				{
					_peerCounter++;
					peerID = _peerCounter;
				}

				IdToPeer[peerID] = fromPeer;
				PeerToId[fromPeer] = peerID;

				if (IsServer)
				{
					EnqueueEvent(new PeerConnectedEvent(peerID));
				}
				else
				{
					EnqueueEvent(new ClientConnectedEvent());
				}
			}
			else if (eventType == ENetConnection.EventType.Disconnect)
			{
				if (fromPeer == null) { PT.PrintWarn("Disconnect received but peer is null, return"); return; }
				IdToPeer.TryRemove(peerID, out _);
				PeerToId.TryRemove(fromPeer, out _);
				if (IsServer)
				{
					EnqueueEvent(new PeerDisconnectedEvent(peerID));
				}
				else
				{
					EnqueueEvent(new ClientDisconnectedEvent());
				}
			}
			else if (eventType == ENetConnection.EventType.Receive)
			{
				Interlocked.Exchange(ref _lastMessageTicks, DateTime.UtcNow.Ticks);
				if (fromPeer == null) { PT.PrintWarn("Message received but peer is null, return"); return; }
				while (fromPeer.GetAvailablePacketCount() > 0)
				{
					int pkf = fromPeer.GetPacketFlags();
					TransferMode m = pkf switch
					{
						(int)ENetPacketPeer.FlagReliable => TransferMode.Reliable,
						(int)ENetPacketPeer.FlagUnreliableFragment => TransferMode.UnreliableOrdered,
						(int)ENetPacketPeer.FlagUnsequenced => TransferMode.Unreliable,
						_ => TransferMode.Unreliable,
					};
					byte[] data = fromPeer.GetPacket();

					EnqueueEvent(new MessageReceivedEvent(peerID, data, m));
				}
			}
			else if (eventType == ENetConnection.EventType.Error)
			{
				PT.PrintErr("Client error");
				EnqueueEvent(new ClientErrorEvent(NetInstanceErrorEnum.NetworkError));
			}
			else if (eventType == ENetConnection.EventType.None) return;

			serviceData = _peer.Service(0);
		}
	}

	private void CheckSilence()
	{
		// Only check silence in client
		if (IsServer) return;

		long lastTicks = Interlocked.Read(ref _lastMessageTicks);
		double elapsedSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastTicks).TotalSeconds;

		bool currentlySilent = elapsedSeconds > SilenceTimeoutSeconds;

		if (currentlySilent != IsSilence)
		{
			IsSilence = currentlySilent;
			if (IsSilence)
			{
				PT.PrintErr("[!] Network connection has gone silent");
			}
			else
			{
				PT.Print("[i] Network connection resumed.");
			}
		}
	}

	private void ProcessActionQueue()
	{
		while (_actionQueue.TryDequeue(out Action? action))
		{
			try
			{
				action?.Invoke();
			}
			catch (Exception ex)
			{
				GD.PushError("Error processing queued action: ", ex);
			}
		}
	}

	private void EnqueueEvent(DeferredNetworkEvent e)
	{
		_mainThreadEventQueue.Enqueue(e);
		if (Interlocked.CompareExchange(ref _mainThreadDrainScheduled, 1, 0) == 0)
		{
			Callable.From(DrainEvents).CallDeferred();
		}
	}

	private void DrainEvents()
	{
		try
		{
			while (_mainThreadEventQueue.TryDequeue(out DeferredNetworkEvent? e))
			{
				switch (e)
				{
					case PeerConnectedEvent connected:
						string dataToken = Guid.NewGuid().ToString() + Guid.NewGuid().ToString();
						_dataServerTokens[connected.PeerID] = dataToken;
						PeerConnected?.Invoke(connected.PeerID);
						break;
					case PeerDisconnectedEvent disconnected:
						_dataServerTokens.Remove(disconnected.PeerID);
						PeerDisconnected?.Invoke(disconnected.PeerID);
						break;
					case ClientConnectedEvent:
						ClientConnected?.Invoke();
						break;
					case ClientDisconnectedEvent:
						ClientDisconnected?.Invoke();
						break;
					case ClientErrorEvent error:
						ClientError?.Invoke(error.Error);
						break;
					case MessageReceivedEvent msg:
						MessageReceived?.Invoke(msg.PeerID, msg.Data, msg.TransferMode);
						break;
				}
			}
		}
		finally
		{
			Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);

			if (!_mainThreadEventQueue.IsEmpty && Interlocked.CompareExchange(ref _mainThreadDrainScheduled, 1, 0) == 0)
			{
				Callable.From(DrainEvents).CallDeferred();
			}
		}
	}

	public bool IsPeerConnected(int peerID)
	{
		return IdToPeer.ContainsKey(peerID);
	}

	public delegate void MessageReceivedHandler(int peerID, byte[] data, TransferMode transferMode);

	public enum NetInstanceErrorEnum
	{
		DataChannelConnectFailure,
		DataChannelAuthFailure,
		NetworkError
	}

	private abstract record DeferredNetworkEvent;
	private record PeerConnectedEvent(int PeerID) : DeferredNetworkEvent;
	private record PeerDisconnectedEvent(int PeerID) : DeferredNetworkEvent;
	private record ClientConnectedEvent : DeferredNetworkEvent;
	private record ClientDisconnectedEvent : DeferredNetworkEvent;
	private record ClientErrorEvent(NetInstanceErrorEnum Error) : DeferredNetworkEvent;
	private record MessageReceivedEvent(int PeerID, byte[] Data, TransferMode TransferMode) : DeferredNetworkEvent;
}

public enum AuthorityMode
{
	Server,
	Authority,
	Any
}


public enum TransferMode
{
	Reliable = (int)ENetPacketPeer.FlagReliable,
	UnreliableOrdered = (int)ENetPacketPeer.FlagUnreliableFragment,
	Unreliable = (int)ENetPacketPeer.FlagUnsequenced,
}

public class NetworkException(string err) : Exception(err) { }
