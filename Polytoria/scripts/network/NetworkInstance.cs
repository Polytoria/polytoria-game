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
	private const ENetConnection.CompressionMode CompressionMode = ENetConnection.CompressionMode.Fastlz;
	private const int BandwidthInLimit = 0;
	private const int BandwidthOutLimit = 30 * 1024;
	private const int BandwidthPerPlayer = 200 * 1024; // 200 KB/s per player
	private long _lastMessageTicks = DateTime.UtcNow.Ticks;
	private readonly Dictionary<int, string> _dataServerTokens = [];

	private const int DefaultCapacity = 67;
	private const int DefaultPort = 21441;
	private const int PollIntervalMs = 5;
	private const int MaxActionsPerPoll = 256;
	private const int MaxInboundEventsBeforeActions = 256;
	private const int MaxInboundPacketsBeforeActions = 256;

	private readonly ENetConnection _peer;

	private readonly ConcurrentQueue<PendingNetworkAction> _actionQueue = new();
	internal readonly ConcurrentDictionary<int, ENetPacketPeer> IdToPeer = [];
	internal readonly ConcurrentDictionary<ENetPacketPeer, int> PeerToId = [];

	private readonly ConcurrentQueue<DeferredNetworkEvent> _mainThreadEventQueue = new();
	private int _mainThreadDrainScheduled = 0;
	private Task? _networkTask;

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
	private volatile bool _shutdownd = false;

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
		_networkTask = Task.Run(NetworkLoop);
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
		if (_shutdownd) return;
		_actionQueue.Enqueue(PendingNetworkAction.Send(targetID, data, transferMode, transferChannel));
	}

	public void DisconnectPeer(int targetID, bool force = false)
	{
		if (_shutdownd) return;
		_actionQueue.Enqueue(PendingNetworkAction.Disconnect(targetID, force));
	}

	public void Shutdown()
	{
		if (_shutdownd) return;
		_shutdownd = true;

		try
		{
			_networkTask?.Wait(1000);
		}
		catch (AggregateException ex)
		{
			GD.PushError("Network loop shutdown error: ", ex.Flatten());
		}

		try
		{
			foreach ((_, ENetPacketPeer pk) in IdToPeer)
			{
				if (GodotObject.IsInstanceValid(pk)) pk.PeerDisconnect();
			}
			if (GodotObject.IsInstanceValid(_peer))
			{
				_peer.Flush();
				_peer.Destroy();
			}
		}
		finally
		{
			ClearQueue(_actionQueue);
			ClearQueue(_mainThreadEventQueue);
			IdToPeer.Clear();
			PeerToId.Clear();
			_dataServerTokens.Clear();
			PeerConnected = null;
			PeerDisconnected = null;
			ClientConnected = null;
			ClientDisconnected = null;
			ClientError = null;
			MessageReceived = null;
		}
	}

	public void BroadcastMessage(byte[] data, TransferMode transferMode, int transferChannel = 0, int[]? except = null)
	{
		if (_shutdownd) return;
		_actionQueue.Enqueue(PendingNetworkAction.Broadcast(data, transferMode, transferChannel, except));
	}

	internal void BroadcastMessageExcept(byte[] data, TransferMode transferMode, int transferChannel, int except)
	{
		if (_shutdownd) return;
		_actionQueue.Enqueue(PendingNetworkAction.BroadcastExcept(data, transferMode, transferChannel, except));
	}

	private void NetworkLoop()
	{
		while (true)
		{
			if (_shutdownd) return;
			if (!GodotObject.IsInstanceValid(_peer)) return;
			try
			{
				ProcessNetwork();
				ProcessActionQueue();
				CheckSilence();
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
			}
			if (_actionQueue.IsEmpty)
			{
				Thread.Sleep(PollIntervalMs);
			}
		}
	}

	public double PopStatistic(ENetConnection.HostStatistic hs)
	{
		if (_shutdownd || !GodotObject.IsInstanceValid(_peer)) return 0;
		return _peer.PopStatistic(hs);
	}

	private void ProcessNetwork()
	{
		int processedEvents = 0;
		int processedPackets = 0;
		Godot.Collections.Array serviceData = _peer.Service();
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
					EnqueueEvent(DeferredNetworkEvent.PeerConnected(peerID));
				}
				else
				{
					EnqueueEvent(DeferredNetworkEvent.ClientConnected());
				}
			}
			else if (eventType == ENetConnection.EventType.Disconnect)
			{
				if (fromPeer == null) { PT.PrintWarn("Disconnect received but peer is null, return"); return; }
				IdToPeer.TryRemove(peerID, out _);
				PeerToId.TryRemove(fromPeer, out _);
				if (IsServer)
				{
					EnqueueEvent(DeferredNetworkEvent.PeerDisconnected(peerID));
				}
				else
				{
					EnqueueEvent(DeferredNetworkEvent.ClientDisconnected());
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

					EnqueueEvent(DeferredNetworkEvent.MessageReceived(peerID, data, m));
					processedPackets++;
					if (processedPackets >= MaxInboundPacketsBeforeActions)
					{
						ProcessActionQueue();
						processedPackets = 0;
					}
				}
			}
			else if (eventType == ENetConnection.EventType.Error)
			{
				PT.PrintErr("Client error");
				EnqueueEvent(DeferredNetworkEvent.ClientError(NetInstanceErrorEnum.NetworkError));
			}
			else if (eventType == ENetConnection.EventType.None) return;

			processedEvents++;
			if (processedEvents >= MaxInboundEventsBeforeActions)
			{
				ProcessActionQueue();
				processedEvents = 0;
			}

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
		for (int processed = 0; processed < MaxActionsPerPoll && _actionQueue.TryDequeue(out PendingNetworkAction action); processed++)
		{
			try
			{
				if (action.Kind == NetworkActionKind.Broadcast)
				{
					foreach ((int id, ENetPacketPeer broadcastPeer) in IdToPeer)
					{
						bool isExcluded = id == action.ExceptPeer ||
							action.ExceptPeers is not null && Array.IndexOf(action.ExceptPeers, id) >= 0;
						if (isExcluded || !broadcastPeer.IsActive()) continue;
						broadcastPeer.Send(action.TransferChannel, action.Data!, (int)action.TransferMode);
					}
					continue;
				}

				ENetPacketPeer? peer = GetPacketPeerFromId(action.TargetID);
				if (peer == null)
				{
					GD.PushWarning(action.TargetID, " doesn't exist");
					continue;
				}

				if (action.Kind == NetworkActionKind.Send)
				{
					Error err = peer.Send(action.TransferChannel, action.Data!, (int)action.TransferMode);
					if (err != Error.Ok)
					{
						GD.PushError("Send error: ", err);
					}
				}
				else if (action.Force)
				{
					peer.PeerDisconnectNow();
				}
				else
				{
					peer.PeerDisconnect();
				}
			}
			catch (Exception ex)
			{
				GD.PushError("Error processing queued action: ", ex);
			}
		}
	}

	private void EnqueueEvent(DeferredNetworkEvent e)
	{
		if (_shutdownd) return;
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
			if (_shutdownd)
			{
				ClearQueue(_mainThreadEventQueue);
				return;
			}

			while (_mainThreadEventQueue.TryDequeue(out DeferredNetworkEvent e))
			{
				switch (e)
				{
					case { Kind: DeferredNetworkEventKind.PeerConnected }:
						string dataToken = Guid.NewGuid().ToString() + Guid.NewGuid().ToString();
						_dataServerTokens[e.PeerID] = dataToken;
						PeerConnected?.Invoke(e.PeerID);
						break;
					case { Kind: DeferredNetworkEventKind.PeerDisconnected }:
						_dataServerTokens.Remove(e.PeerID);
						PeerDisconnected?.Invoke(e.PeerID);
						break;
					case { Kind: DeferredNetworkEventKind.ClientConnected }:
						ClientConnected?.Invoke();
						break;
					case { Kind: DeferredNetworkEventKind.ClientDisconnected }:
						ClientDisconnected?.Invoke();
						break;
					case { Kind: DeferredNetworkEventKind.ClientError }:
						ClientError?.Invoke(e.Error);
						break;
					case { Kind: DeferredNetworkEventKind.MessageReceived }:
						MessageReceived?.Invoke(e.PeerID, e.Data!, e.TransferMode);
						break;
				}
			}
		}
		finally
		{
			Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);

			if (!_shutdownd && !_mainThreadEventQueue.IsEmpty && Interlocked.CompareExchange(ref _mainThreadDrainScheduled, 1, 0) == 0)
			{
				Callable.From(DrainEvents).CallDeferred();
			}
		}
	}

	private static void ClearQueue<T>(ConcurrentQueue<T> queue)
	{
		while (queue.TryDequeue(out _)) { }
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

	private enum NetworkActionKind : byte
	{
		Send,
		Broadcast,
		Disconnect
	}

	private readonly struct PendingNetworkAction
	{
		public NetworkActionKind Kind { get; init; }
		public int TargetID { get; init; }
		public byte[]? Data { get; init; }
		public TransferMode TransferMode { get; init; }
		public int TransferChannel { get; init; }
		public int ExceptPeer { get; init; }
		public int[]? ExceptPeers { get; init; }
		public bool Force { get; init; }

		public static PendingNetworkAction Send(int targetID, byte[] data, TransferMode mode, int channel) => new()
		{
			Kind = NetworkActionKind.Send,
			TargetID = targetID,
			Data = data,
			TransferMode = mode,
			TransferChannel = channel,
			ExceptPeer = -1
		};

		public static PendingNetworkAction Broadcast(byte[] data, TransferMode mode, int channel, int[]? exceptPeers) => new()
		{
			Kind = NetworkActionKind.Broadcast,
			Data = data,
			TransferMode = mode,
			TransferChannel = channel,
			ExceptPeer = -1,
			ExceptPeers = exceptPeers
		};

		public static PendingNetworkAction BroadcastExcept(byte[] data, TransferMode mode, int channel, int exceptPeer) => new()
		{
			Kind = NetworkActionKind.Broadcast,
			Data = data,
			TransferMode = mode,
			TransferChannel = channel,
			ExceptPeer = exceptPeer
		};

		public static PendingNetworkAction Disconnect(int targetID, bool force) => new()
		{
			Kind = NetworkActionKind.Disconnect,
			TargetID = targetID,
			ExceptPeer = -1,
			Force = force
		};
	}

	private enum DeferredNetworkEventKind : byte
	{
		PeerConnected,
		PeerDisconnected,
		ClientConnected,
		ClientDisconnected,
		ClientError,
		MessageReceived
	}

	private readonly struct DeferredNetworkEvent
	{
		public DeferredNetworkEventKind Kind { get; init; }
		public int PeerID { get; init; }
		public byte[]? Data { get; init; }
		public TransferMode TransferMode { get; init; }
		public NetInstanceErrorEnum Error { get; init; }

		public static DeferredNetworkEvent PeerConnected(int peerID) => new() { Kind = DeferredNetworkEventKind.PeerConnected, PeerID = peerID };
		public static DeferredNetworkEvent PeerDisconnected(int peerID) => new() { Kind = DeferredNetworkEventKind.PeerDisconnected, PeerID = peerID };
		public static DeferredNetworkEvent ClientConnected() => new() { Kind = DeferredNetworkEventKind.ClientConnected };
		public static DeferredNetworkEvent ClientDisconnected() => new() { Kind = DeferredNetworkEventKind.ClientDisconnected };
		public static DeferredNetworkEvent ClientError(NetInstanceErrorEnum error) => new() { Kind = DeferredNetworkEventKind.ClientError, Error = error };
		public static DeferredNetworkEvent MessageReceived(int peerID, byte[] data, TransferMode mode) => new()
		{
			Kind = DeferredNetworkEventKind.MessageReceived,
			PeerID = peerID,
			Data = data,
			TransferMode = mode
		};
	}
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
