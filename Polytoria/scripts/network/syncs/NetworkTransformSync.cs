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
using System.Collections.Generic;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Client.Networking;

[Internal]
public partial class NetworkTransformSync : Instance
{
	private const double BatchInterval = 0.05;

	internal NetworkService NetService = null!;
	private readonly Dictionary<string, PendingTransform> _pendingTransforms = [];

	// Pending Transform batch update
	private readonly Dictionary<string, PendingBatchTransform> _pendingBatchUpdate = [];
	private double _batchTimer = 0.0;

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
		if (NetService.IsServer)
		{
			_batchTimer += delta;

			if (_batchTimer >= BatchInterval)
			{
				if (_pendingBatchUpdate.Count > 0)
					BroadcastBatchedTransforms();
				_pendingBatchUpdate.Clear();
				_batchTimer = 0.0;
			}
		}
	}

	public void SyncAllTransformToPeer(int peerID)
	{
		NetworkedObject[] allNetObjs = NetService.Root.GetReplicateDescendants();

		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackTransforms(allNetObjs)));
		RpcId(peerID, nameof(NetRecvChunk), rawData, true);
	}

	public void SendChunk(NetworkedObject[] netObjs, Player plr)
	{
		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackTransforms(netObjs)));
		RpcId(plr.PeerID, nameof(NetRecvChunk), rawData, false);
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
		base.PreDelete();
	}

	public void SendTransformToServer(Dynamic dyn, TransformPayloadDto payload, bool lerpTransform = false)
	{
		// Return if not ready
		if (!dyn.IsNetworkReady || !dyn.Root.IsLoaded) return;

		// Ignore in creator
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return;

		// Check authority
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return;

		string objID = dyn.NetworkedObjectID;

		RpcId(1, nameof(NetRecvTransformOnServer), objID, payload, lerpTransform);
	}

	public void BroadcastTransformFromServer(Dynamic dyn, TransformPayloadDto payload, bool lerpTransform, int excludePeer = -1, bool reliable = true)
	{
		if (!NetService.IsServer) return;
		if (!dyn.IsNetworkReady) return;
		string objID = dyn.NetworkedObjectID;

		SetPendingBatch(objID, new(payload, lerpTransform, excludePeer)
		{
			Reliable = reliable,
			Forced = true,
			Position = dyn.GetGlobalTransform().Origin,
			AlwaysRelevant = dyn is Player
		}, forced: true);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered, CallLocal = false)]
	private void NetRecvTransformOnServer(string objID, TransformPayloadDto transform, bool lerpTransform)
	{
		int fromPeer = RemoteSenderId;

		if (NetService.Root.GetNetObjectFromID(objID) is Dynamic dyn)
		{
			if (!CheckDynAuthor(dyn, fromPeer))
			{
				PT.PrintErr($"[Net] Unauthorized transform from peer {fromPeer} for {objID}");
				return;
			}

			if (transform?.Data is not { Length: 16 }) return;

			// server-side validation
			if (!dyn.TransformNetworkCheck(transform))
			{
				PT.PrintErr($"[Net] Invalid transform from peer {fromPeer}");

				// Send correction back
				SendUpdateTransform(dyn, true, fromPeer);
				return;
			}
			TransformPayloadDto processed = dyn.TransformNetworkPass(fromPeer, transform);

			// If is equal approx to last, return
			if (processed.IsEqualApprox(dyn.GetLocalTransform()))
				return;

			// Update on server
			dyn.UpdateTransformFromNet(processed, false, lerpTransform);

			// Add to batch pending
			SetPendingBatch(objID, new(processed, lerpTransform, fromPeer)
			{
				Reliable = false,
				Position = dyn.GetGlobalTransform().Origin,
				AlwaysRelevant = dyn is Player
			});
		}
	}

	private readonly List<BatchTransformData> _reliableScratch = [];
	private readonly List<int> _reliableExcludeScratch = [];
	private readonly List<BatchTransformData> _unreliableScratch = [];
	private readonly List<int> _unreliableExcludeScratch = [];
	private readonly List<Vector3> _unreliablePositionScratch = [];
	private readonly List<bool> _unreliableAlwaysScratch = [];
	private readonly List<BatchTransformData> _peerScratch = [];

	private void BroadcastBatchedTransforms()
	{
		if (NetService.NetInstance == null || _pendingBatchUpdate.Count == 0) return;

		_reliableScratch.Clear();
		_reliableExcludeScratch.Clear();
		_unreliableScratch.Clear();
		_unreliableExcludeScratch.Clear();
		_unreliablePositionScratch.Clear();
		_unreliableAlwaysScratch.Clear();

		foreach (var (k, pending) in _pendingBatchUpdate)
		{
			BatchTransformData batchData = new(
				k,
				pending.Transform.Data,
				pending.LerpTransform
			);

			if (pending.Reliable)
			{
				_reliableScratch.Add(batchData);
				_reliableExcludeScratch.Add(pending.ExcludePeer);
			}
			else
			{
				_unreliableScratch.Add(batchData);
				_unreliableExcludeScratch.Add(pending.ExcludePeer);
				_unreliablePositionScratch.Add(pending.Position);
				_unreliableAlwaysScratch.Add(pending.AlwaysRelevant);
			}
		}

		SendBatchesPerPeer(_reliableScratch, _reliableExcludeScratch, null, null, nameof(NetRecvBatchedTransformsReliable), TransferMode.Reliable);
		SendBatchesPerPeer(_unreliableScratch, _unreliableExcludeScratch, _unreliablePositionScratch, _unreliableAlwaysScratch, nameof(NetRecvBatchedTransformsUnreliable), TransferMode.UnreliableOrdered);
	}

	private void SendBatchesPerPeer(List<BatchTransformData> entries, List<int> excludes, List<Vector3>? positions, List<bool>? alwaysRelevant, string rpcName, TransferMode transferMode)
	{
		if (entries.Count == 0) return;

		NetworkInstance net = NetService.NetInstance;
		float radius = NetService.ReplicationRadius;
		bool filterByDistance = radius > 0f && positions != null && alwaysRelevant != null;
		float radiusSq = radius * radius;
		byte[]? fullPacket = null;

		foreach (int peerID in net.PeerIds)
		{
			Vector3 peerPosition = default;
			bool peerFiltered = false;
			if (filterByDistance && NetService.Root.Players.GetPlayerFromPeerID(peerID) is { } player)
			{
				peerPosition = player.GetGlobalTransform().Origin;
				peerFiltered = true;
			}

			bool excluded = false;
			for (int i = 0; i < excludes.Count; i++)
			{
				if (excludes[i] == peerID)
				{
					excluded = true;
					break;
				}
			}

			if (!excluded && !peerFiltered)
			{
				fullPacket ??= BuildRpcPacket(rpcName, SerializeUtils.Serialize<BatchTransformData[]>([.. entries]));
				net.SendMessage(peerID, fullPacket, transferMode);
				continue;
			}

			_peerScratch.Clear();
			for (int i = 0; i < entries.Count; i++)
			{
				if (excludes[i] == peerID) continue;
				if (peerFiltered && !alwaysRelevant![i] && peerPosition.DistanceSquaredTo(positions![i]) > radiusSq) continue;
				_peerScratch.Add(entries[i]);
			}

			if (_peerScratch.Count == 0) continue;

			byte[] packet = BuildRpcPacket(rpcName, SerializeUtils.Serialize<BatchTransformData[]>([.. _peerScratch]));
			net.SendMessage(peerID, packet, transferMode);
		}
	}

	private void SetPendingBatch(string objID, PendingBatchTransform entry, bool forced = false)
	{
		if (_pendingBatchUpdate.TryGetValue(objID, out var existing) && existing.Forced && !forced)
			return;

		// Skip if transform is not changed enough to matter
		// existing.Transform can be null here. Don't ask me why.
		if (!forced && existing.Transform != null && existing.Transform.IsEqualApprox(entry.Transform))
			return;

		_pendingBatchUpdate[objID] = entry;
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvBatchedTransformsReliable(byte[] transformsRaw)
	{
		RecvBatchedTransforms(transformsRaw, true);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvBatchedTransformsUnreliable(byte[] transformsRaw)
	{
		RecvBatchedTransforms(transformsRaw, false);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void RecvBatchedTransforms(byte[] transformsRaw, bool isReliable)
	{
		BatchTransformData[]? transforms = SerializeUtils.Deserialize<BatchTransformData[]>(transformsRaw);
		if (transforms == null) return;
		foreach (var data in transforms)
		{
			if (NetService.Root.GetNetObjectFromID(data.ObjID) is Dynamic dyn)
			{
				dyn.UpdateTransformFromNet(TransformPayloadDto.FromArray(data.Transform), isReliable, data.Lerp);
			}
		}
	}

	private struct PendingBatchTransform(TransformPayloadDto transform, bool lerpTransform, int excludePeer)
	{
		public TransformPayloadDto Transform = transform;
		public bool LerpTransform = lerpTransform;
		public int ExcludePeer = excludePeer;
		public bool Reliable = false;
		public bool Forced = false;
		public Vector3 Position = default;
		public bool AlwaysRelevant = false;
	}

	private struct PendingTransform()
	{
		public TransformPayloadDto Transform = default!;
		public int FromPeer;
		public int ToPeer = -1;
	}

	[MemoryPackable]
	public partial struct BatchTransformData
	{
		public string ObjID = null!;
		public byte[] Transform = null!;
		public bool Lerp = false;

		public BatchTransformData(string objID, byte[] transform, bool lerp)
		{
			ObjID = objID;
			Transform = transform;
			Lerp = lerp;
		}
	}
}
