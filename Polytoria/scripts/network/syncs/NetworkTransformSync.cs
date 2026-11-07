// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Networking;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.Compression;
using Polytoria.Utils.DTOs;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using static Polytoria.Client.Networking.TransformPacket;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Client.Networking;

[Internal]
public partial class NetworkTransformSync : Instance
{
	internal const double TickInterval = 1.0 / 20;
	internal static readonly ulong TickSteps = (ulong)Mathf.Max(1, Mathf.Round(Engine.PhysicsTicksPerSecond * TickInterval));
	internal const int TransformChannel = 3;

	private const int SnapshotBudgetBytes = 70 * 1024;
	private const int SnapshotTickBytes = (int)(SnapshotBudgetBytes * TickInterval);
	private const int SnapshotBurstBytes = SnapshotTickBytes * 10;
	private const float ImportantSpeed = 2f;
	private const float ImportantPriority = 100f;
	private const float UrgentPriority = 10000f;
	private const float JumpSpeed = 8f;
	private const uint MaxWaitTicks = 10;

	private const double ViewInterval = 0.2;
	private const double ViewTimeout = 2;
	private const int IndexSweepTicks = 20;
	private const double IndexReuseDelay = 10;
	private const double IndexRequestRetry = 1;
	private const int OwnershipTicks = 5;
	private const double PreviousOwnerGrace = 1;
	private const double BridgeTimeout = 1;

	private const double MaxFutureTime = 0.1;
	private const double MaxPastTime = 1.0;
	private const int MaxUploadPacketSize = 1280;
	private const double UploadBandwidthShare = 0.6;
	private const double RejectLogInterval = 1;
	internal static readonly int UploadBudgetBytes = Mathf.Min(MaxUploadPacketSize,
		(int)(NetworkInstance.BandwidthOutLimit * TickInterval * UploadBandwidthShare));

	internal NetworkService NetService = null!;
	internal readonly AutoOwnership Ownership = new();
	private readonly Dictionary<string, PendingTransform> _pendingTransforms = [];

	// Pending Transform batch update
	private readonly Dictionary<string, PendingBatchTransform> _pendingBatchUpdate = [];
	private readonly Dictionary<string, PendingServerTransform> _pendingServerTransforms = [];
	private readonly Dictionary<int, Dictionary<string, byte[]>> _sentChunks = [];
	private readonly Dictionary<string, ushort> _transformIndices = [];
	private readonly Dictionary<ushort, string> _transformIndexIDs = [];
	private readonly Dictionary<string, double> _indexRequests = [];
	private readonly Dictionary<int, PeerSnapshot> _peerSnapshots = [];
	private readonly Dictionary<Physical, (double Start, double Lag)> _bridges = [];
	private readonly Queue<(ushort Index, double FreedAt)> _freeIndices = new();
	private readonly List<DueEntry> _dueEntries = [];
	private readonly Dictionary<ushort, (double Time, Vector3 Position, Vector3 Velocity)> _lastQueued = [];
	private readonly HashSet<Dynamic> _scriptedMovers = [];
	private readonly Dictionary<int, PeerView> _peerViews = [];
	private double _nextView;
	private uint _snapshotTick;
	private int _nextIndex;
	private byte _indexEpoch = 0;
	private double _tickTimer = 0.0;
	private ulong _lastTickStep;
	private double _frameDelta = 0.0;
	private double _lastRejectLog = double.MinValue;

	private static readonly bool _useNetworkLog = false;

	static NetworkTransformSync()
	{
		if (Globals.IsInGDEditor) return;
		_useNetworkLog = OS.HasFeature("netlog");
	}

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		_frameDelta = delta;
		_tickTimer += delta;
		ulong tickStep = Engine.GetPhysicsFrames() / TickSteps;
		if (tickStep == _lastTickStep && _tickTimer < TickInterval * 2) return;
		_lastTickStep = tickStep;
		_tickTimer = 0;
		Dynamic.FlushTransformUpdates();

		if (NetService.IsServer)
		{
			SendBridges();
			if (_scriptedMovers.Count > 0)
				_scriptedMovers.RemoveWhere(dyn => dyn.SendScriptedRest());
			if (_pendingBatchUpdate.Count > 0)
				BroadcastBatchedTransforms();
			_pendingBatchUpdate.Clear();
			SendSnapshots();
			if (_snapshotTick % OwnershipTicks == 0)
			{
				Ownership.UpdateOwners(NetService.Root);
			}
			if (_snapshotTick % IndexSweepTicks == 0)
			{
				FreeDeletedIndices();
			}
		}
		else
		{
			FlushTransformsToServer();
			SendView();
		}
	}

	public void SyncAllTransformToPeer(int peerID)
	{
		NetBatchTransformData[] data = PackTransforms(NetService.Root.GetReplicateDescendants());

		if (_sentChunks.Remove(peerID, out Dictionary<string, byte[]>? sent))
		{
			data = [.. data.Where(d => !sent.TryGetValue(d.NetID, out byte[]? v) || !v.AsSpan().SequenceEqual(d.Value))];
		}

		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(data));
		RpcId(peerID, nameof(NetRecvChunk), rawData, true);
	}

	public void SendChunk(NetworkedObject[] netObjs, Player plr)
	{
		NetBatchTransformData[] data = PackTransforms(netObjs);
		Dictionary<string, byte[]> sent = new(data.Length);

		foreach (NetBatchTransformData d in data)
		{
			sent[d.NetID] = d.Value;
		}

		_sentChunks[plr.PeerID] = sent;

		List<TransformIndexData> indices = [];
		foreach (var (objID, index) in _transformIndices)
		{
			if (NetService.Root.GetNetObjectFromID(objID) != null)
			{
				indices.Add(new(objID, index));
			}
		}
		RpcId(plr.PeerID, nameof(NetRecvTransformIndices), (int)_indexEpoch, SerializeUtils.Serialize<TransformIndexData[]>([.. indices]));

		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(data));
		RpcId(plr.PeerID, nameof(NetRecvChunk), rawData, false);
	}

	internal void ForgetPeer(int peerID)
	{
		_sentChunks.Remove(peerID);
		_peerSnapshots.Remove(peerID);
		_peerViews.Remove(peerID);
		Ownership.ForgetPeer(peerID);
	}

	internal (Vector3 Velocity, Vector3 AngularVelocity) StartBridge(Physical physical)
	{
		double now = NetService.Root.NetTime;
		var motion = physical.GetNetMotion();
		if (!_bridges.TryGetValue(physical, out var bridge))
		{
			bridge = (now, Mathf.Clamp(now - motion.Time, 0, BridgeTimeout));
			physical.BeginNetBridge(motion.Velocity, motion.AngularVelocity);
		}
		_bridges[physical] = (now, bridge.Lag);
		return (motion.Velocity, motion.AngularVelocity);
	}

	internal void EndBridge(Physical physical, bool freeze = true)
	{
		if (_bridges.Remove(physical))
		{
			physical.EndNetBridge(freeze);
		}
	}

	private void SendBridges()
	{
		if (_bridges.Count == 0) return;

		double now = NetService.Root.NetTime;
		foreach (var (physical, bridge) in _bridges.ToArray())
		{
			if (physical.IsDeleted)
			{
				_bridges.Remove(physical);
				continue;
			}
			if (now - bridge.Start > BridgeTimeout)
			{
				EndBridge(physical);
				continue;
			}

			if (_pendingBatchUpdate.TryGetValue(physical.NetworkedObjectID, out var pending) && pending.Reliable) continue;

			var state = physical.GetNetBridgeState();
			TransformPayloadDto payload = TransformPayloadDto.FromGDTransform(state.Transform);
			SetPendingBatch(physical.NetworkedObjectID, new(payload, true, -1)
			{
				Reliable = false,
				Forced = true,
				Time = NetService.Root.PhysicsNetTime - bridge.Lag,
				Velocity = state.Velocity
			}, forced: true);
		}
	}

	internal void AddScriptedMover(Dynamic dyn)
	{
		_scriptedMovers.Add(dyn);
	}

	public void BroadcastChunk(NetworkedObject[] netObjs)
	{
		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackTransforms(netObjs)));
		Rpc(nameof(NetRecvChunk), rawData, false);
	}

	private static NetBatchTransformData[] PackTransforms(NetworkedObject[] netObjs)
	{
		List<NetBatchTransformData> data = [];
		foreach (NetworkedObject item in netObjs)
		{
			if (item is Dynamic dyn)
			{
				data.Add(new()
				{
					NetID = dyn.NetworkedObjectID,
					Value = TransformPayloadDto.ToArray(dyn.GetLocalTransform())
				});
			}
		}
		return [.. data];
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvChunk(byte[] rawBytes, bool isFirstInit)
	{
		NetBatchTransformData[] netObjsData = SerializeUtils.Deserialize<NetBatchTransformData[]>(ZstdCompressionUtils.Decompress(rawBytes))!;

		foreach (NetBatchTransformData item in netObjsData)
		{
			// There might be newer pending transforms
			if (_pendingTransforms.ContainsKey(item.NetID)) { continue; }
			RecvUpdateTransformHandler(item.NetID, TransformPayloadDto.FromArray(item.Value), 1, true, false);
		}

		if (isFirstInit)
		{
			NetService.NetTransformSyncd();
		}
	}

	public void SendUpdateTransform(Dynamic dyn, bool isReliable = false, int sendTo = 0, bool lerpTransform = false)
	{
		// If not ready, return
		if (!dyn.IsNetworkReady) return;
		if (!dyn.Root.IsLoaded) return;

		// If is in creator, return
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return;

		// Check if self has the network authority
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return;

		TransformPayloadDto payload = TransformPayloadDto.FromGDTransform(dyn.GetLocalTransform());
		string objID = dyn.NetworkedObjectID;

		if (sendTo != 0)
		{
			if (isReliable)
			{
				RpcId(sendTo, nameof(NetRecvUpdateTransformReliable), objID, payload, lerpTransform);
			}
			else
			{
				RpcId(sendTo, nameof(NetRecvUpdateTransform), objID, payload, lerpTransform);
			}
		}
		else
		{
			if (isReliable)
			{
				if (_useNetworkLog) { PT.Print($"[Net] [Transform] {dyn.NetworkPath} Reliable update"); }

				Rpc(nameof(NetRecvUpdateTransformReliable), objID, payload, lerpTransform);
			}
			else
			{
				Rpc(nameof(NetRecvUpdateTransform), objID, payload, lerpTransform);
			}
		}
	}


	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvUpdateTransform(string objID, TransformPayloadDto transform, bool lerpTransform)
	{
		RecvUpdateTransformHandler(objID, transform, RemoteSenderId, false, lerpTransform);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvUpdateTransformReliable(string objID, TransformPayloadDto transform, bool lerpTransform)
	{
		RecvUpdateTransformHandler(objID, transform, RemoteSenderId, true, lerpTransform);
	}

	internal byte[]? GetReplicateTransform(Dynamic dyn)
	{
		if (!dyn.IsNetworkReady || !dyn.Root.IsLoaded) return null;
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return null;
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return null;

		return TransformPayloadDto.ToArray(dyn.GetLocalTransform());
	}

	internal void RecvReplicateTransform(string objID, byte[] transform, int fromPeer)
	{
		RecvUpdateTransformHandler(objID, TransformPayloadDto.FromArray(transform), fromPeer, true, false);
	}

	private void RecvUpdateTransformHandler(string objID, TransformPayloadDto transform, int fromPeer, bool isReliable, bool lerpTransform)
	{
		if (NetService.Root.GetNetObjectFromID(objID) is Dynamic dyn)
		{
			dyn.UpdateTransformFromNet(dyn.TransformNetworkPass(fromPeer, transform), isReliable, lerpTransform);
		}
		else
		{
			if (_useNetworkLog) { PT.Print($"[Net] [Transform] [?] {objID} Pending"); }
			_pendingTransforms[objID] = new() { Transform = transform, FromPeer = fromPeer };
		}
	}

	private static bool CheckDynAuthor(Dynamic dyn, int fromPeer)
	{
		return CheckAuthority(fromPeer, dyn.NetworkAuthority) || CheckAuthority(fromPeer, dyn.NetTransformAuthority);
	}

	internal void DropPendingTransform(string objID)
	{
		_pendingTransforms.Remove(objID);
	}

	internal void ApplyPendingTransforms(Dynamic dyn)
	{
		string objID = dyn.NetworkedObjectID;

		if (_pendingTransforms.TryGetValue(objID, out var pending))
		{
			dyn.UpdateTransformFromNet(pending.Transform, true, false);
			_pendingTransforms.Remove(objID);
		}
	}

	public override void PreDelete()
	{
		SetProcess(false);
		_pendingTransforms.Clear();
		_pendingBatchUpdate.Clear();
		_pendingServerTransforms.Clear();
		_lastQueued.Clear();
		_sentChunks.Clear();
		_peerSnapshots.Clear();
		_transformIndices.Clear();
		_transformIndexIDs.Clear();
		_indexRequests.Clear();
		_freeIndices.Clear();
		base.PreDelete();
	}

	public void SendTransformToServer(Dynamic dyn, TransformPayloadDto payload, bool lerpTransform = false,
		bool rest = false, Vector3? velocity = null)
	{
		// Return if not ready
		if (!dyn.IsNetworkReady || !dyn.Root.IsLoaded) return;

		// Ignore in creator
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return;

		// Check authority
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return;

		string objID = dyn.NetworkedObjectID;

		double since = _pendingServerTransforms.TryGetValue(objID, out PendingServerTransform existing) ? existing.Since : World.LocalClock;
		_pendingServerTransforms[objID] = new(payload, lerpTransform, World.PhysicsClock, rest, velocity, since);
	}

	private void FlushTransformsToServer()
	{
		if (_pendingServerTransforms.Count == 0 || !NetService.Root.IsClockSynced) return;

		double baseTime = NetService.Root.NetTime;
		double clockOffset = baseTime - World.LocalClock;
		int capacity = Mathf.Min(_pendingServerTransforms.Count, MaxUploadEntries);
		byte[] raw = NewUploadBatch(baseTime, capacity);
		byte[]? restRaw = null;
		int count = 0;
		int restCount = 0;
		string? avatarID = NetService.Root.Players.LocalPlayer?.NetworkedObjectID;
		KeyValuePair<string, PendingServerTransform>[] order =
			[.. _pendingServerTransforms.OrderBy(p => p.Key == avatarID ? double.MinValue : p.Value.Since)];
		int budget = UploadBudgetBytes - SnapshotHeaderSize;
		foreach (var (objID, pending) in order)
		{
			if (NetService.Root.GetNetObjectFromID(objID) is not Dynamic dyn || !CheckDynAuthor(dyn, NetService.LocalPeerID))
			{
				_pendingServerTransforms.Remove(objID);
				continue;
			}
			if (!_transformIndices.TryGetValue(objID, out ushort index))
			{
				RequestTransformIndex(objID);
				continue;
			}
			if (!pending.Rest && SnapshotEntrySize > budget) continue;
			_pendingServerTransforms.Remove(objID);
			if (!pending.Rest) budget -= SnapshotEntrySize;
			Vector3 velocity = pending.Velocity ?? dyn.Motion.DeriveVelocity(pending.Transform.Position, pending.LocalTime);
			if (!pending.LerpTransform || pending.Rest) velocity = Vector3.Zero;
			double time = pending.LocalTime + clockOffset;
			byte flags = (byte)((pending.LerpTransform ? LerpFlag : 0) | (pending.Rest ? RestFlag : 0));
			if (pending.Rest)
			{
				restRaw ??= NewUploadBatch(baseTime, capacity);
				restCount = AddUploadEntry(nameof(NetRecvRestBatchOnServer), restRaw, restCount, index,
					pending.Transform.Data, time, baseTime, flags, velocity, dyn.AckedServerWrite);
			}
			else
			{
				count = AddUploadEntry(nameof(NetRecvTransformBatchOnServer), raw, count, index,
					pending.Transform.Data, time, baseTime, flags, velocity, dyn.AckedServerWrite);
			}
		}
		SendUploadBatch(nameof(NetRecvTransformBatchOnServer), raw, count);
		if (restRaw != null) SendUploadBatch(nameof(NetRecvRestBatchOnServer), restRaw, restCount);
	}

	private void RequestTransformIndex(string objID)
	{
		if (_indexRequests.TryGetValue(objID, out double requestedAt) && World.LocalClock - requestedAt < IndexRequestRetry) return;
		_indexRequests[objID] = World.LocalClock;
		RpcId(1, nameof(NetRecvTransformIndexRequest), objID);
	}

	private byte[] NewUploadBatch(double baseTime, int capacity)
	{
		byte[] raw = new byte[SnapshotHeaderSize + capacity * SnapshotEntrySize];
		raw[0] = _indexEpoch;
		BinaryPrimitives.WriteDoubleLittleEndian(raw.AsSpan(1), baseTime);
		return raw;
	}

	private int AddUploadEntry(string rpcName, byte[] raw, int count, ushort index, byte[] transform,
		double time, double baseTime, byte flags, Vector3 velocity, byte writeSeq)
	{
		WriteEntry(raw.AsSpan(SnapshotHeaderSize + count * SnapshotEntrySize), index, transform, time, baseTime, flags, velocity, writeSeq);
		count++;
		if (count < MaxUploadEntries) return count;
		SendUploadBatch(rpcName, raw, count);
		return 0;
	}

	private void SendUploadBatch(string rpcName, byte[] raw, int count)
	{
		if (count == 0) return;
		RpcId(1, rpcName, raw.AsSpan(0, SnapshotHeaderSize + count * SnapshotEntrySize).ToArray());
	}

	public void BroadcastTransformFromServer(Dynamic dyn, TransformPayloadDto payload, bool lerpTransform,
		int excludePeer = -1, bool reliable = true, bool rest = false, Vector3? velocity = null)
	{
		if (!NetService.IsServer) return;
		if (!dyn.IsNetworkReady) return;
		string objID = dyn.NetworkedObjectID;

		if (_pendingBatchUpdate.TryGetValue(objID, out var existing) && existing.Reliable)
		{
			reliable = true;
			if (!existing.LerpTransform) lerpTransform = false;
		}

		SetPendingBatch(objID, new(payload, lerpTransform, excludePeer)
		{
			Reliable = reliable,
			Forced = true,
			Rest = rest,
			Velocity = velocity,
			Time = NetService.Root.PhysicsNetTime,
			WriteSeq = dyn.ServerWriteSeq
		}, forced: true);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, CallLocal = false, TransferChannel = TransformChannel)]
	private void NetRecvTransformIndexRequest(string objID)
	{
		if (NetService.Root.GetNetObjectFromID(objID) is not Dynamic dyn || !CheckDynAuthor(dyn, RemoteSenderId)) return;

		if (_transformIndices.TryGetValue(objID, out ushort index))
		{
			RpcId(RemoteSenderId, nameof(NetRecvTransformIndices), (int)_indexEpoch,
				SerializeUtils.Serialize<TransformIndexData[]>([new(objID, index)]));
			return;
		}

		if (!AddTransformIndex(objID, out index))
		{
			ResetTransformIndices();
			AddTransformIndex(objID, out index);
		}
		Rpc(nameof(NetRecvTransformIndices), (int)_indexEpoch, SerializeUtils.Serialize<TransformIndexData[]>([new(objID, index)]));
	}

	private void SendView()
	{
		if (World.LocalClock < _nextView || !NetService.Root.IsLoaded) return;
		if ((Engine.GetMainLoop() as SceneTree)?.Root.GetCamera3D() is not Camera3D camera) return;
		_nextView = World.LocalClock + ViewInterval;
		Transform3D view = camera.GlobalTransform;
		byte[] raw = new byte[24];
		WriteVector3(raw.AsSpan(0), view.Origin);
		WriteVector3(raw.AsSpan(12), -view.Basis.Z);
		RpcId(1, nameof(NetRecvViewOnServer), raw);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Unreliable, CallLocal = false, TransferChannel = TransformChannel)]
	private void NetRecvViewOnServer(byte[] raw)
	{
		if (!NetService.IsServer || raw.Length < 24) return;
		Vector3 origin = ReadVector3(raw.AsSpan(0));
		Vector3 forward = ReadVector3(raw.AsSpan(12));
		if (!origin.IsFinite() || !forward.IsFinite() || forward.Length() < 0.01f) return;
		_peerViews[RemoteSenderId] = new(origin, forward.Normalized(), World.LocalClock);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered, CallLocal = false, TransferChannel = TransformChannel)]
	private void NetRecvTransformBatchOnServer(byte[] raw)
	{
		ReceiveTransformBatchOnServer(RemoteSenderId, raw);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, CallLocal = false)]
	private void NetRecvRestBatchOnServer(byte[] raw)
	{
		ReceiveTransformBatchOnServer(RemoteSenderId, raw);
	}

	private void ReceiveTransformBatchOnServer(int fromPeer, byte[] raw)
	{
		if (raw.Length < SnapshotHeaderSize || raw.Length > SnapshotHeaderSize + MaxUploadEntries * SnapshotEntrySize || raw[0] != _indexEpoch) return;
		double baseTime = BinaryPrimitives.ReadDoubleLittleEndian(raw.AsSpan(1));
		double maxAge = MaxUploadAge(fromPeer);

		for (int offset = SnapshotHeaderSize; offset + SnapshotEntrySize <= raw.Length; offset += SnapshotEntrySize)
		{
			UploadEntry entry = ReadEntry(raw.AsSpan(offset, SnapshotEntrySize), baseTime);
			if (!_transformIndexIDs.TryGetValue(entry.Index, out string? objID)) continue;
			ReceiveTransformOnServer(fromPeer, objID, entry.Transform, (entry.Flags & LerpFlag) != 0, entry.Time,
				(entry.Flags & RestFlag) != 0, entry.Velocity, maxAge, entry.WriteSeq);
		}
	}

	private double MaxUploadAge(int peerID)
	{
		if (NetService.NetInstance?.PeerRoundTrips.TryGetValue(peerID, out double roundTrip) != true) return MaxPastTime;
		return Mathf.Min(roundTrip + 2 * TickInterval + _frameDelta, MaxPastTime);
	}

	private double ServerWriteGrace(int peerID)
	{
		double roundTrip = NetService.NetInstance?.PeerRoundTrips.GetValueOrDefault(peerID) ?? 0;
		return roundTrip / 2 + TickInterval;
	}

	private void ReceiveTransformOnServer(int fromPeer, string objID, TransformPayloadDto transform, bool lerpTransform,
		double time, bool rest, Vector3 velocity, double maxAge, byte writeSeq)
	{
		if (NetService.Root.GetNetObjectFromID(objID) is Dynamic dyn)
		{
			if (!CheckDynAuthor(dyn, fromPeer))
			{
				if (fromPeer == dyn.PreviousNetTransformAuthority && World.LocalClock - dyn.NetTransformAuthorityChangedAt < PreviousOwnerGrace) return;
				LogRejected($"[Net] Unauthorized transform from peer {fromPeer} for {objID}");
				return;
			}

			if (transform?.Data is not { Length: 20 }) return;
			if (dyn.HasPendingNetWrite) return;
			if (dyn.IsAwaitingWriteAck(writeSeq)) return;
			time = Mathf.Clamp(time, NetService.Root.NetTime - maxAge, NetService.Root.NetTime + MaxFutureTime);
			if (lerpTransform && time < dyn.LastNetSampleTime) return;
			if (dyn.LastServerWrite > NetService.Root.NetTime - MaxPastTime && time < dyn.LastServerWrite + ServerWriteGrace(fromPeer)) return;

			// server-side validation
			if (!dyn.TransformNetworkCheck(transform))
			{
				LogRejected($"[Net] Invalid transform from peer {fromPeer}");

				// Send correction back
				SendUpdateTransform(dyn, true, fromPeer);
				return;
			}
			TransformPayloadDto processed = dyn.TransformNetworkPass(fromPeer, transform);
			velocity = dyn.LimitNetVelocity(processed.Position, time, velocity);

			// If is equal approx to last, return
			if (!rest && !lerpTransform && processed.IsEqualApprox(TransformPayloadDto.FromGDTransform(dyn.GetLocalTransform())))
				return;

			// Update on server
			if (dyn is Physical physical) EndBridge(physical);
			dyn.UpdateTransformFromNet(processed, false, lerpTransform, time, 1, rest, velocity);

			// Add to batch pending
			SetPendingBatch(objID, new(processed, lerpTransform, fromPeer) { Reliable = rest, Time = time, Rest = rest, Velocity = velocity });
		}
	}

	private void LogRejected(string message)
	{
		if (World.LocalClock - _lastRejectLog < RejectLogInterval) return;
		_lastRejectLog = World.LocalClock;
		PT.PrintErr(message);
	}

	private void BroadcastGroupedBatch(List<BatchTransformData> batch, string rpcName, TransferMode transferMode, int excludePeer = -1)
	{
		if (NetService.NetInstance == null || batch.Count == 0) return;

		byte[] packet = BuildRpcPacket(rpcName, SerializeUtils.Serialize<BatchTransformData[]>([.. batch]));

		if (excludePeer == -1)
		{
			NetService.NetInstance.BroadcastMessage(packet, transferMode);
		}
		else
		{
			NetService.NetInstance.BroadcastMessageExcept(packet, transferMode, 0, excludePeer);
		}
	}

	private void BroadcastBatchedTransforms()
	{
		if (NetService.NetInstance == null || _pendingBatchUpdate.Count == 0) return;

		Dictionary<int, List<BatchTransformData>> reliableExcept = [];

		List<BatchTransformData> reliableAll = [];
		List<BatchTransformData> unreliableAll = [];

		foreach (var (k, pending) in _pendingBatchUpdate)
		{
			Vector3 velocity = Vector3.Zero;
			if (pending.Velocity is Vector3 given)
			{
				velocity = given;
			}
			else if (NetService.Root.GetNetObjectFromID(k) is Dynamic dyn)
			{
				velocity = dyn.Motion.DeriveVelocity(pending.Transform.Position, pending.Time);
			}
			BatchTransformData batchData = new(
				k,
				pending.Transform.Data,
				pending.LerpTransform,
				pending.ExcludePeer,
				pending.Time,
				pending.Rest,
				pending.LerpTransform ? velocity : Vector3.Zero
			);
			batchData.WriteSeq = pending.WriteSeq;

			if (pending.Reliable)
			{
				DropQueuedSnapshot(k);
				if (pending.ExcludePeer == -1)
				{
					reliableAll.Add(batchData);
				}
				else
				{
					GetOrCreateBatch(reliableExcept, pending.ExcludePeer).Add(batchData);
				}
			}
			else
			{
				unreliableAll.Add(batchData);
			}
		}

		BroadcastGroupedBatch(reliableAll, nameof(NetRecvBatchedTransformsReliable), TransferMode.Reliable);
		QueueSnapshot(unreliableAll);

		// Send reliable batches
		foreach (var (peerID, batch) in reliableExcept)
		{
			BroadcastGroupedBatch(batch, nameof(NetRecvBatchedTransformsReliable), TransferMode.Reliable, peerID);
		}
	}

	private void QueueSnapshot(List<BatchTransformData> batch)
	{
		if (batch.Count == 0) return;

		List<TransformIndexData> newIndices = [];
		List<(ushort Index, SnapshotEntry Entry, int Owner)> entries = new(batch.Count);
		foreach (BatchTransformData data in batch)
		{
			if (!_transformIndices.TryGetValue(data.ObjID, out ushort index))
			{
				if (!AddTransformIndex(data.ObjID, out index))
				{
					ResetTransformIndices();
					QueueSnapshot(batch);
					return;
				}
				newIndices.Add(new(data.ObjID, index));
			}

			Dynamic? dyn = NetService.Root.GetNetObjectFromID(data.ObjID) as Dynamic;
			bool bridged = dyn is Physical physical && _bridges.ContainsKey(physical);
			int owner = dyn == null || bridged ? 1 : dyn.NetTransformAuthority;
			Vector3 spin = GetSpin(dyn);
			bool lerp = data.Lerp;
			bool urgent = !data.Lerp;
			if (dyn != null)
			{
				Vector3 position = ReadVector3(data.Transform);
				if (data.Lerp && _lastQueued.TryGetValue(index, out var last) && data.Time > last.Time)
				{
					Vector3 gravity = dyn.Motion.GetLocalGravity();
					if (!data.Rest && NetMotion.ShouldSnap(last.Time, last.Position, last.Velocity, data.Time, position, data.Velocity, gravity))
					{
						lerp = false;
						urgent = true;
					}
					else
					{
						urgent = (data.Velocity - last.Velocity - gravity * (float)Mathf.Min(data.Time - last.Time, 2 * TickInterval)).Length() > JumpSpeed;
					}
				}
				_lastQueued[index] = (data.Time, position, data.Velocity);
			}
			Vector3 angular = Vector3.Zero;
			if (data.Lerp && !data.Rest && dyn != null)
			{
				angular = dyn.GetNetParentBasis().Orthonormalized().Transposed() * spin;
			}
			Vector3 globalPosition = dyn?.GetGlobalPosition() ?? Vector3.Zero;
			SnapshotEntry entry = new(data.Transform, data.Time, lerp, data.Rest, data.Velocity, globalPosition,
				GetMaxPeriod(spin), _snapshotTick, angular, urgent);
			entries.Add((index, entry, owner));
		}

		if (newIndices.Count > 0)
		{
			Rpc(nameof(NetRecvTransformIndices), (int)_indexEpoch, SerializeUtils.Serialize<TransformIndexData[]>([.. newIndices]));
		}

		foreach (int peerID in NetService.NetInstance!.PeerIds)
		{
			if (!_peerSnapshots.TryGetValue(peerID, out PeerSnapshot? peer))
			{
				peer = new();
				_peerSnapshots[peerID] = peer;
			}
			foreach (var item in entries)
			{
				if (item.Owner == peerID) continue;
				SnapshotEntry merged = item.Entry;
				if (peer.Queued.TryGetValue(item.Index, out SnapshotEntry queued))
				{
					merged = queued.Urgent && !item.Entry.Urgent ? queued : item.Entry with { Since = queued.Since };
				}
				peer.Queued[item.Index] = merged;
			}
		}
	}

	private bool AddTransformIndex(string objID, out ushort index)
	{
		if (!TryTakeIndex(out index)) return false;
		_transformIndices[objID] = index;
		_transformIndexIDs[index] = objID;
		return true;
	}

	private bool TryTakeIndex(out ushort index)
	{
		if (_freeIndices.TryPeek(out var free) && World.LocalClock - free.FreedAt >= IndexReuseDelay)
		{
			index = _freeIndices.Dequeue().Index;
			return true;
		}
		index = (ushort)_nextIndex;
		bool available = _nextIndex < ushort.MaxValue;
		_nextIndex++;
		return available;
	}

	private void FreeDeletedIndices()
	{
		double now = World.LocalClock;
		foreach (var (objID, index) in _transformIndices.Where(p => NetService.Root.GetNetObjectFromID(p.Key) == null).ToArray())
		{
			_transformIndices.Remove(objID);
			_transformIndexIDs.Remove(index);
			_lastQueued.Remove(index);
			_freeIndices.Enqueue((index, now));
			foreach (PeerSnapshot peer in _peerSnapshots.Values)
			{
				peer.Forget(index);
			}
		}
	}

	private static Vector3 GetSpin(Dynamic? dyn)
	{
		if (dyn is RigidBody body && !body.IsFrozen) return body.GDRigidBody.AngularVelocity;
		if (dyn != null && dyn.TryGetNetVelocity(out _, out Vector3 angular)) return angular;
		return Vector3.Zero;
	}

	private static uint GetMaxPeriod(Vector3 spin)
	{
		float turn = spin.Length() * (float)TickInterval;
		if (turn * 4 <= Mathf.Pi / 2) return 4;
		if (turn * 2 <= Mathf.Pi / 2) return 2;
		return 1;
	}

	private static Vector3 GetSpinToSend(SnapshotEntry entry, uint waited)
	{
		return entry.Angular.Length() * (waited + 1) * TickInterval > Mathf.Pi / 2 ? entry.Angular : Vector3.Zero;
	}

	private void SendSnapshots()
	{
		if (NetService.NetInstance == null) return;
		_snapshotTick++;
		double baseTime = NetService.Root.NetTime;

		foreach (var (peerID, peer) in _peerSnapshots)
		{
			peer.Credit = Mathf.Min(peer.Credit + SnapshotTickBytes, SnapshotBurstBytes);
			if (peer.Queued.Count == 0) continue;

			Vector3? focus = NetService.Root.Players.GetPlayerFromPeerID(peerID)?.GetGlobalPosition();
			bool hasView = _peerViews.TryGetValue(peerID, out var view) && World.LocalClock - view.Time < ViewTimeout;
			Vector3 origin = focus ?? Vector3.Zero;
			_dueEntries.Clear();
			int dueBytes = 0;
			foreach (var (index, entry) in peer.Queued)
			{
				uint period = 4;
				if (focus is Vector3 f)
				{
					bool moving = entry.Velocity.Length() > ImportantSpeed;
					period = Math.Min(peer.UpdatePeriod(index, entry.Position.DistanceTo(f), moving), entry.MaxPeriod);
				}
				uint waited = _snapshotTick - entry.Since;
				if (entry.Urgent || waited > period || (_snapshotTick + index) % period == 0)
				{
					int size = CompactEntrySize(entry.Transform, entry.Velocity, GetSpinToSend(entry, waited), origin);
					float priority;
					if (entry.Urgent)
					{
						priority = UrgentPriority * (waited + 1);
					}
					else
					{
						bool important = IsImportant(entry) || peer.Fast.Contains(index) || waited >= MaxWaitTicks;
						priority = (float)waited / period * (important ? ImportantPriority : 1f);
					}
					if (hasView && waited < MaxWaitTicks) priority *= view.Relevance(entry.Position);
					_dueEntries.Add(new(index, period, priority, size));
					dueBytes += size;
				}
			}
			if (_dueEntries.Count == 0) continue;

			uint throttle = (uint)((dueBytes + SnapshotTickBytes - 1) / SnapshotTickBytes);
			if (dueBytes > peer.Credit)
			{
				_dueEntries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
			}

			byte[] packet = new byte[MaxSnapshotPacketSize];
			int length = CompactHeaderSize;
			foreach (DueEntry due in _dueEntries)
			{
				SnapshotEntry entry = peer.Queued[due.Index];
				if (due.Size > peer.Credit + (entry.Urgent ? SnapshotBurstBytes : 0)) continue;
				if (length + due.Size > MaxSnapshotPacketSize)
				{
					SendSnapshotPacket(peerID, packet, length, baseTime, origin, throttle);
					length = CompactHeaderSize;
				}

				peer.Queued.Remove(due.Index);
				peer.Credit -= due.Size;
				if (entry.Velocity.Length() > ImportantSpeed) peer.Fast.Add(due.Index);
				else peer.Fast.Remove(due.Index);
				int periodBits = due.Period >= 4 ? 2 : due.Period >= 2 ? 1 : 0;
				byte flags = (byte)((entry.Lerp ? LerpFlag : 0) | (periodBits << PeriodShift) | (entry.Rest ? RestFlag : 0));
				Vector3 spin = GetSpinToSend(entry, _snapshotTick - entry.Since);
				length += WriteCompactEntry(packet.AsSpan(length), due.Index, entry.Transform, entry.Time, baseTime, flags, entry.Velocity, spin, origin);
			}
			if (length > CompactHeaderSize)
			{
				SendSnapshotPacket(peerID, packet, length, baseTime, origin, throttle);
			}
		}
	}

	private void SendSnapshotPacket(int peerID, byte[] packet, int length, double baseTime, Vector3 origin, uint throttle)
	{
		packet[0] = _indexEpoch;
		BinaryPrimitives.WriteDoubleLittleEndian(packet.AsSpan(1), baseTime);
		WriteVector3(packet.AsSpan(9), origin);
		packet[21] = (byte)Math.Min(throttle, 255u);
		byte[] data = BuildRpcPacket(nameof(NetRecvTransformSnapshot), packet.AsSpan(0, length).ToArray());
		NetService.NetInstance!.SendMessage(peerID, data, TransferMode.Unreliable, TransformChannel);
	}

	private static bool IsImportant(SnapshotEntry entry)
	{
		return entry.Velocity == Vector3.Zero || entry.Velocity.Length() > ImportantSpeed;
	}

	private void DropQueuedSnapshot(string objID)
	{
		if (!_transformIndices.TryGetValue(objID, out ushort index)) return;
		foreach (PeerSnapshot peer in _peerSnapshots.Values)
		{
			peer.Queued.Remove(index);
		}
	}

	private void ResetTransformIndices()
	{
		_transformIndices.Clear();
		_transformIndexIDs.Clear();
		_freeIndices.Clear();
		_lastQueued.Clear();
		_peerSnapshots.Clear();
		_nextIndex = 0;
		_indexEpoch++;
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, TransferChannel = TransformChannel)]
	private void NetRecvTransformIndices(int epoch, byte[] indicesRaw)
	{
		if (epoch != _indexEpoch)
		{
			_indexEpoch = (byte)epoch;
			_transformIndexIDs.Clear();
			_transformIndices.Clear();
			_indexRequests.Clear();
		}

		TransformIndexData[]? indices = SerializeUtils.Deserialize<TransformIndexData[]>(indicesRaw);
		if (indices == null) return;
		foreach (TransformIndexData data in indices)
		{
			if (_transformIndexIDs.TryGetValue(data.Index, out string? previous) &&
				_transformIndices.TryGetValue(previous, out ushort previousIndex) && previousIndex == data.Index)
			{
				_transformIndices.Remove(previous);
			}
			if (_transformIndices.TryGetValue(data.ObjID, out ushort oldIndex) && oldIndex != data.Index)
			{
				_transformIndexIDs.Remove(oldIndex);
			}
			_transformIndexIDs[data.Index] = data.ObjID;
			_transformIndices[data.ObjID] = data.Index;
			_indexRequests.Remove(data.ObjID);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Unreliable, TransferChannel = TransformChannel)]
	private void NetRecvTransformSnapshot(byte[] raw)
	{
		if (raw.Length < CompactHeaderSize || raw[0] != _indexEpoch) return;
		double baseTime = BinaryPrimitives.ReadDoubleLittleEndian(raw.AsSpan(1));
		Vector3 origin = ReadVector3(raw.AsSpan(9));
		NetService.Root.SnapshotThrottle = raw[21];

		int offset = CompactHeaderSize;
		while (offset + CompactEntryBaseSize <= raw.Length)
		{
			ReadOnlySpan<byte> entry = raw.AsSpan(offset);
			byte flags = entry[2];
			int mode = (flags >> PositionModeShift) & 3;
			bool hasVelocity = (flags & VelocityFlag) != 0;
			bool hasSpin = (flags & SpinFlag) != 0;
			int size = CompactEntryBaseSize + (mode == 0 ? 12 : 6) + (hasVelocity ? 6 : 0) + (hasSpin ? 3 : 0);
			if (offset + size > raw.Length) break;
			offset += size;

			ushort index = BinaryPrimitives.ReadUInt16LittleEndian(entry);
			double time = baseTime + BinaryPrimitives.ReadInt16LittleEndian(entry[3..]) / 1000.0;
			ulong rotation = BinaryPrimitives.ReadUInt64LittleEndian(entry[5..]);
			int cursor = CompactEntryBaseSize;
			Vector3 position;
			if (mode == 0)
			{
				position = ReadVector3(entry[cursor..]);
				cursor += 12;
			}
			else
			{
				short x = BinaryPrimitives.ReadInt16LittleEndian(entry[cursor..]);
				short y = BinaryPrimitives.ReadInt16LittleEndian(entry[(cursor + 2)..]);
				short z = BinaryPrimitives.ReadInt16LittleEndian(entry[(cursor + 4)..]);
				position = new Vector3(x, y, z) * PositionStep + (mode == 1 ? origin : Vector3.Zero);
				cursor += 6;
			}
			Vector3 velocity = Vector3.Zero;
			if (hasVelocity)
			{
				velocity = ReadHalfVector3(entry[cursor..]);
				cursor += 6;
			}
			Vector3 spin = hasSpin ? new Vector3((sbyte)entry[cursor], (sbyte)entry[cursor + 1], (sbyte)entry[cursor + 2]) * SpinStep : Vector3.Zero;
			TransformPayloadDto transform = new(position, rotation);

			if (!_transformIndexIDs.TryGetValue(index, out string? objID) || NetService.Root.GetNetObjectFromID(objID) is not Dynamic dyn) continue;
			bool lerp = (flags & LerpFlag) != 0;
			if (dyn.NetTransformAuthority == NetService.LocalPeerID && !dyn.IsNetGaining &&
				(lerp || time <= dyn.NetTransformAuthorityChangedNetTime - NetMotion.MaxGap))
			{
				continue;
			}

			int period = 1 << ((flags >> PeriodShift) & 3);
			dyn.UpdateTransformFromNet(transform, false, lerp, time, period, (flags & RestFlag) != 0, velocity, spin);
		}
	}

	private void SetPendingBatch(string objID, PendingBatchTransform entry, bool forced = false)
	{
		if (_pendingBatchUpdate.TryGetValue(objID, out var existing) && existing.Forced && !forced)
			return;

		// Skip if transform is not changed enough to matter
		// existing.Transform can be null here. Don't ask me why.
		if (!forced && !entry.Rest && !entry.LerpTransform && existing.Transform != null && existing.Transform.IsEqualApprox(entry.Transform))
			return;

		_pendingBatchUpdate[objID] = entry;
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvBatchedTransformsReliable(byte[] transformsRaw)
	{
		BatchTransformData[]? transforms = SerializeUtils.Deserialize<BatchTransformData[]>(transformsRaw);
		if (transforms == null) return;
		foreach (var data in transforms)
		{
			if (data.ExcludePeer == NetService.LocalPeerID) continue;
			if (NetService.Root.GetNetObjectFromID(data.ObjID) is Dynamic dyn)
			{
				dyn.UpdateTransformFromNet(TransformPayloadDto.FromArray(data.Transform), true, data.Lerp, data.Time, 1, data.Rest, data.Velocity);
				if (data.WriteSeq != 0 && dyn.NetTransformAuthority == NetService.LocalPeerID) dyn.AckedServerWrite = data.WriteSeq;
			}
			else if (_pendingTransforms.ContainsKey(data.ObjID))
			{
				_pendingTransforms[data.ObjID] = new() { Transform = TransformPayloadDto.FromArray(data.Transform), FromPeer = RemoteSenderId };
			}
		}
	}

	private static List<BatchTransformData> GetOrCreateBatch(Dictionary<int, List<BatchTransformData>> dict, int excludePeer)
	{
		if (!dict.TryGetValue(excludePeer, out var list))
		{
			list = [];
			dict[excludePeer] = list;
		}

		return list;
	}

	private struct PendingBatchTransform(TransformPayloadDto transform, bool lerpTransform, int excludePeer)
	{
		public TransformPayloadDto Transform = transform;
		public bool LerpTransform = lerpTransform;
		public int ExcludePeer = excludePeer;
		public bool Reliable = false;
		public bool Forced = false;
		public bool Rest = false;
		public double Time = 0;
		public Vector3? Velocity = null;
		public byte WriteSeq = 0;
	}

	private readonly record struct PendingServerTransform(
		TransformPayloadDto Transform,
		bool LerpTransform,
		double LocalTime,
		bool Rest,
		Vector3? Velocity,
		double Since);

	private readonly record struct SnapshotEntry(
		byte[] Transform,
		double Time,
		bool Lerp,
		bool Rest,
		Vector3 Velocity,
		Vector3 Position,
		uint MaxPeriod,
		uint Since,
		Vector3 Angular,
		bool Urgent);

	private readonly record struct DueEntry(ushort Index, uint Period, float Priority, int Size);

	private readonly record struct PeerView(Vector3 Origin, Vector3 Forward, double Time)
	{
		private const float ViewCos = 0.3f;
		private const float ViewNear = 100;
		private const float BehindWeight = 0.25f;
		private const float MinRelevance = 0.05f;

		public float Relevance(Vector3 position)
		{
			Vector3 offset = position - Origin;
			float distance = offset.Length();
			if (distance < 0.001f) return 1;
			float facing = offset.Dot(Forward) / distance > ViewCos ? 1 : BehindWeight;
			return Mathf.Max(facing * Mathf.Min(1, ViewNear / distance), MinRelevance);
		}
	}

	private sealed class PeerSnapshot
	{
		private const float NearDistance = 256;
		private const float MidDistance = 512;
		private const float TierHysteresis = 0.1f;

		public readonly Dictionary<ushort, SnapshotEntry> Queued = [];
		public readonly HashSet<ushort> Fast = [];
		public int Credit = SnapshotBurstBytes;
		private readonly Dictionary<ushort, uint> _periods = [];

		public void Forget(ushort index)
		{
			Queued.Remove(index);
			Fast.Remove(index);
			_periods.Remove(index);
		}

		public uint UpdatePeriod(ushort index, float distance, bool moving)
		{
			uint closer = PeriodFor(distance / (1 + TierHysteresis));
			uint farther = PeriodFor(distance / (1 - TierHysteresis));
			uint period = _periods.TryGetValue(index, out uint current) ? Math.Clamp(current, closer, farther) : PeriodFor(distance);
			if (moving && current != 0 && period > current) period = current;
			_periods[index] = period;
			return period;
		}

		private static uint PeriodFor(float distance) => distance < NearDistance ? 1u : distance < MidDistance ? 2u : 4u;
	}

	private struct PendingTransform()
	{
		public TransformPayloadDto Transform = default!;
		public int FromPeer;
		public int ToPeer = -1;
	}

	[MemoryPackable]
	public partial struct TransformIndexData
	{
		public string ObjID = null!;
		public ushort Index = 0;

		public TransformIndexData(string objID, ushort index)
		{
			ObjID = objID;
			Index = index;
		}
	}

	[MemoryPackable]
	public partial struct BatchTransformData
	{
		public string ObjID = null!;
		public byte[] Transform = null!;
		public bool Lerp = false;
		public int ExcludePeer = -1;
		public double Time = 0;
		public bool Rest = false;
		public Vector3 Velocity = Vector3.Zero;
		public byte WriteSeq = 0;

		public BatchTransformData(string objID, byte[] transform, bool lerp, int excludePeer, double time, bool rest, Vector3 velocity)
		{
			ObjID = objID;
			Transform = transform;
			Lerp = lerp;
			ExcludePeer = excludePeer;
			Time = time;
			Rest = rest;
			Velocity = velocity;
		}
	}
}
