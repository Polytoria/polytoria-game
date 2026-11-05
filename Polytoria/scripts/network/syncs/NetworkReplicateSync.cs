// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.Compression;
using System;
using System.Collections.Generic;
using System.Linq;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Networking.Synchronizers;

[Internal]
public sealed partial class NetworkReplicateSync : Instance
{
	private const int PlaceReplicationChunkSize = 300;
	private const double RemovedKeepTime = 30;
	internal NetworkService NetService = null!;

	// Queue for pending replicate data (waiting for node to spawn)
	private readonly Dictionary<string, List<NetReplicateData>> _pendingReplicates = [];

	public int InstanceLoadedCount = 1;
	public int InstanceToBeLoadedCount = 0;
	public event Action<int, int>? InstanceLoadedProgress;
	private readonly Dictionary<string, (ulong Seq, double Time)> _removedIDs = [];
	private ulong _recvSeq;
	private double _removedSweep;
	private readonly HashSet<NetReplicateData> _worldReplicateSet = [];
	private readonly Dictionary<NetworkedObject, bool> _pendingSends = [];

	private static readonly bool _useNetworkLog = false;

	static NetworkReplicateSync()
	{
		_useNetworkLog = OS.HasFeature("netlog");
	}

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public void SyncPlaceToPlayer(Player plr)
	{
		World game = NetService.Root;
		NetworkedObject[] netObjs = game.GetReplicateDescendants();

		game.NetSendAllPropUpdate(plr.PeerID);

		SendChunk(netObjs, plr, true);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void NetRecvChunk(byte[] rawBytes, bool isPlaceReplicate)
	{
		_recvSeq++;
		ulong seq = _recvSeq;

		// Wait frame for all deletion to finish
		await Globals.Singleton.WaitFrame();
		await Globals.Singleton.WaitFrame();

		NetReplicateData[] netObjsData = SerializeUtils.Deserialize<NetReplicateData[]>(ZstdCompressionUtils.Decompress(rawBytes))!;
		NetReplicateData[][] chunked = ArrayUtils.Chunk(netObjsData, PlaceReplicationChunkSize);

		foreach (NetReplicateData data in netObjsData)
		{
			// If already exists, continue
			if (Root.NetworkObjects.ContainsKey(data.NetworkID)) continue;
			if (isPlaceReplicate)
			{
				_worldReplicateSet.Add(data);
				InstanceToBeLoadedCount += 1;
			}
		}

		foreach (NetReplicateData[] chunk in chunked)
		{
			foreach (NetReplicateData data in chunk)
			{
				if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] on the way {data.Name}"); }

				if (data.ParentNodeID == null)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {data.Name} no parent"); }
					CountInstanceLoaded(null);
					continue;
				}

				string parentID = data.ParentNodeID;
				string parentPath = data.ParentNodePath;
				NetworkedObject? parentNode = NetService.Root.GetNetObjectFromID(parentID);

				// Fallback to path;
				parentNode ??= NetService.Root.GetNetObj(parentPath);

				if (IsRemovedAfter(data.NetworkID, seq) || IsRemovedAfter(parentID, seq))
				{
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {data.Name} removed"); }
					if (!IsRemovedAfter(data.NetworkID, seq)) _removedIDs[data.NetworkID] = _removedIDs[parentID];
					if (isPlaceReplicate && _worldReplicateSet.Remove(data)) CountInstanceLoaded(null);
					continue;
				}

				if (parentNode != null)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] Chunk Replicate {data.Name}"); }
					parentNode?.RecvReplicate(data);
				}
				else
				{
					List<NetReplicateData> value = GetPendingReplicates(parentID);
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] Pending {data.Name}"); }
					value.Add(data);
				}
			}
			// hope and prayers
			await Globals.Singleton.WaitFrame();
		}
	}

	public void SendChunk(NetworkedObject[] netObjs, Player plr, bool isPlaceReplicate = false)
	{
		NetService.TransformSync.SendChunk(netObjs, plr);
		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackNetObjs(netObjs)));

		RpcId(plr.PeerID, nameof(NetRecvChunk), rawData, isPlaceReplicate);
	}

	// Fallsafe for pending replicates
	public override void Process(double delta)
	{
		base.Process(delta);

		// Debugging magic key
		if (Input.IsActionJustPressed("magic"))
		{
			foreach (var item in _worldReplicateSet)
			{
				PT.Print(item.Name, " netID: ", item.NetworkID);
			}
		}

		// Check for pending replications
		if (_pendingReplicates.Count == 0)
		{
			SetProcess(false);
			return;
		}

		foreach (string key in _pendingReplicates.Keys.ToArray())
		{
			NetworkedObject? node = NetService.Root.GetNetObjectFromID(key);
			if (node != null)
			{
				CheckLeftoverReplication(node);
			}
		}

		if (_pendingReplicates.Count == 0)
		{
			SetProcess(false);
		}
	}

	public void BroadcastChunk(NetworkedObject[] netObjs)
	{
		if (NetService.NetworkMode != NetworkModeEnum.Client) return;
		NetService.TransformSync.BroadcastChunk(netObjs);
		byte[] rawData = ZstdCompressionUtils.Compress(SerializeUtils.Serialize(PackNetObjs(netObjs)));

		//GD.PushWarning("Broadcast chunk ", netObjs.Length);

		Rpc(nameof(NetRecvChunk), rawData, false);
	}

	public static void MarkChunkOverride(NetworkedObject[] netObjs)
	{
		foreach (NetworkedObject item in netObjs)
		{
			item.ChunkBroadcastOverride = true;
		}
	}

	private static NetReplicateData[] PackNetObjs(IReadOnlyList<NetworkedObject> netObjs)
	{
		return PackNetObjs(netObjs, 0, netObjs.Count);
	}

	private static NetReplicateData[] PackNetObjs(IReadOnlyList<NetworkedObject> netObjs, int start, int count)
	{
		List<NetReplicateData> netObjsData = new(count);

		int end = start + count;
		for (int i = start; i < end; i++)
		{
			NetworkedObject netObj = netObjs[i];
			// If no sync, continue
			if (netObj.GetType().IsDefinedCached(typeof(NoSyncAttribute), false)) continue;

			if (!netObj.ShouldReplicate) continue;

			NetReplicateData data = netObj.GetNetReplicateData();
			data.IsSyncOnce = true;
			netObjsData.Add(data);
			if (_useNetworkLog) { PT.Print($"[Net] Packing {netObj.Name}"); }
		}

		return [.. netObjsData];
	}

	private void CheckLeftoverReplication(NetworkedObject node)
	{
		string netID = node.NetworkedObjectID;

		if (_pendingReplicates.TryGetValue(netID, out List<NetReplicateData>? list))
		{
			foreach (NetReplicateData data in list)
			{
				node.RecvReplicate(data);
			}
			_pendingReplicates.Remove(netID);
		}
	}

	private List<NetReplicateData> GetPendingReplicates(string parentID)
	{
		if (!_pendingReplicates.TryGetValue(parentID, out List<NetReplicateData>? value))
		{
			value = [];
			_pendingReplicates[parentID] = value;
		}

		SetProcess(true);
		return value;
	}

	internal void CountInstanceLoaded(NetworkedObject? obj)
	{
		if (obj != null)
		{
			// If object is not from world replicate, return
			if (!_worldReplicateSet.Remove(new() { NetworkID = obj.NetworkedObjectID })) return;
		}
		InstanceLoadedCount += 1;
		if (InstanceToBeLoadedCount != 0 && !NetService.IsPlaceReplicationDone)
		{
			InstanceLoadedProgress?.Invoke(InstanceLoadedCount, InstanceToBeLoadedCount);

			if (_worldReplicateSet.Count == 0)
			{
				NetService.NetWorldSyncd();
			}
		}
	}

	public void SendNetReplicate(NetworkedObject netObj, bool isSyncOnce = false)
	{
		// if not client, return
		if (NetService.NetworkMode != NetworkModeEnum.Client) return;

		// don't sync if not ready
		if (!netObj.Root.IsLoaded) return;

		// If should not replicate, return
		if (netObj.IsDeleted || !netObj.ShouldReplicate) return;

		if (_useNetworkLog) { PT.Print($"[Net] Send Replicate {netObj.Name}"); }

		if (_pendingSends.Count == 0)
		{
			Callable.From(FlushNetReplicates).CallDeferred();
		}

		_pendingSends[netObj] = isSyncOnce || _pendingSends.GetValueOrDefault(netObj);
	}

	private void FlushNetReplicates()
	{
		if (_pendingSends.Count == 0) return;

		List<NetReplicateData> data = new(_pendingSends.Count);

		foreach ((NetworkedObject netObj, bool isSyncOnce) in _pendingSends)
		{
			if (netObj.IsDeleted || !netObj.ShouldReplicate) continue;

			NetReplicateData netdata = netObj.GetNetReplicateData();
			netdata.IsSyncOnce = isSyncOnce;

			if (netObj is Dynamic dyn)
			{
				netdata.Transform = NetService.TransformSync.GetReplicateTransform(dyn);
			}

			data.Add(netdata);
		}

		_pendingSends.Clear();

		if (data.Count == 0) return;

		Rpc(nameof(NetRecvReplicate), ZstdCompressionUtils.Compress(SerializeUtils.Serialize<NetReplicateData[]>([.. data])));
	}

	public void SendNetReplicateRemove(NetworkedObject netobj, long peerID = -1)
	{
		FlushNetReplicates();

		if (peerID != -1)
		{
			RpcId((int)peerID, nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
		else
		{
			Rpc(nameof(NetRecvReplicateRemove), netobj.NetworkedObjectID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetRecvReplicate(byte[] rawBytes)
	{
		// Ignore if from server itself
		if (NetService.IsServer) return;
		_recvSeq++;
		ulong seq = _recvSeq;

		NetReplicateData[] netObjsData = SerializeUtils.Deserialize<NetReplicateData[]>(ZstdCompressionUtils.Decompress(rawBytes))!;

		foreach (NetReplicateData data in netObjsData)
		{
			if (data.Transform != null)
			{
				NetService.TransformSync.RecvReplicateTransform(data.NetworkID, data.Transform, RemoteSenderId);
			}
		}

		await Globals.Singleton.WaitFrame();

		foreach (NetReplicateData data in netObjsData)
		{
			if (IsRemovedAfter(data.NetworkID, seq) || IsRemovedAfter(data.ParentNodeID, seq))
			{
				NetService.TransformSync.DropPendingTransform(data.NetworkID);
				continue;
			}
			try
			{
				RecvNetReplicate(data);
			}
			catch (Exception ex)
			{
				PT.PrintErr($"[Net] Failed to replicate {data.Name}: {ex}");
			}
		}
	}

	private bool IsRemovedAfter(string? netID, ulong seq)
	{
		return netID != null && _removedIDs.TryGetValue(netID, out var removed) && removed.Seq > seq;
	}

	private void MarkRemoved(string netID, ulong seq)
	{
		double now = World.LocalClock;
		if (now - _removedSweep > 1)
		{
			_removedSweep = now;
			foreach (var (id, removed) in _removedIDs.ToArray())
			{
				if (now - removed.Time > RemovedKeepTime) _removedIDs.Remove(id);
			}
		}
		_removedIDs[netID] = (seq, now);
		NetService.TransformSync.DropPendingTransform(netID);
		if (_pendingReplicates.Remove(netID, out List<NetReplicateData>? children))
		{
			foreach (NetReplicateData child in children)
			{
				MarkRemoved(child.NetworkID, seq);
			}
		}
		foreach (List<NetReplicateData> list in _pendingReplicates.Values)
		{
			list.RemoveAll(d => d.NetworkID == netID);
		}
	}

	private void RecvNetReplicate(NetReplicateData replicateData)
	{
		string parentID = replicateData.ParentNodeID;
		string parentNodePath = replicateData.ParentNodePath;

		if (_useNetworkLog) { PT.Print($"[Net] Recv Replicate {replicateData.Name}"); }

		NetworkedObject? parent = NetService.Root.GetObjectFromID(parentID);

		// Fallback to path
		parent ??= NetService.Root.GetNetObj(parentNodePath);

		if (parent != null)
		{
			NetworkedObject? existingChild = NetService.Root.GetNetObjectFromID(replicateData.NetworkID);

			if (existingChild != null)
			{
				NetworkedObject? currentParent = existingChild.NetworkParent;

				if (currentParent != parent)
				{
					if (parent != existingChild)
					{
						if (_useNetworkLog) { PT.Print($"[Net] [@] Reparent {replicateData.Name}"); }

						existingChild.NetworkParent = parent;
					}
				}
			}
			else
			{
				if (_useNetworkLog) { PT.Print($"[Net] [+] Add {replicateData.Name}"); }
				parent.RecvReplicate(replicateData);
			}
		}
		else
		{
			// Parent not ready yet → queue
			List<NetReplicateData> value = GetPendingReplicates(parentID);
			if (_useNetworkLog) { PT.Print($"[Net] [?] Pending {replicateData.Name}"); }
			value.Add(replicateData);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void NetRecvReplicateRemove(string nodePath)
	{
		if (NetService.IsServer) return;
		_recvSeq++;
		MarkRemoved(nodePath, _recvSeq);

		await Globals.Singleton.WaitFrame();

		NetworkedObject? targetObj = NetService.Root.GetNetObjectFromID(nodePath);

		// If target still exists, remove
		if (targetObj != null)
		{
			if (_useNetworkLog) { PT.Print($"[Net] [-] Remove {targetObj.NetworkPath}"); }
			targetObj.ForceDelete();
		}
		else
		{
			if (_useNetworkLog) { PT.Print($"[Net] [-] [?] Net Obj doesn't exist {nodePath}"); }
		}
	}

	// Flush pending replicates
	public void FlushPendingReplicates(NetworkedObject netObj)
	{
		string netID = netObj.NetworkedObjectID;
		if (_pendingReplicates.TryGetValue(netID, out List<NetReplicateData>? queued))
		{
			_pendingReplicates.Remove(netID);
			if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] Resolve Pending {netID}"); }
			foreach (NetReplicateData r in queued)
			{
				if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {netID} Resolve Pending Replicate {r.Name}"); }

				netObj.RecvReplicate(r);
			}
		}
	}

	public override void PreDelete()
	{
		SetProcess(false);
		_pendingSends.Clear();
		_pendingReplicates.Clear();
		_removedIDs.Clear();
		_worldReplicateSet.Clear();
		InstanceLoadedProgress = null;
		base.PreDelete();
	}
}
