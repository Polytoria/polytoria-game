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
	internal NetworkService NetService = null!;

	// Queue for pending replicate data (waiting for node to spawn)
	private readonly Dictionary<string, List<NetReplicateData>> _pendingReplicates = [];

	public int InstanceLoadedCount = 1;
	public int InstanceToBeLoadedCount = 0;
	public event Action<int, int>? InstanceLoadedProgress;
	private readonly HashSet<string> _removedRef = [];
	private readonly HashSet<NetReplicateData> _worldReplicateSet = [];

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
				if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] on the way {data.NodePath}"); }

				if (data.ParentNodeID == null)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} no parent"); }
					CountInstanceLoaded(null);
					continue;
				}

				string parentID = data.ParentNodeID;
				string parentPath = data.ParentNodePath;
				NetworkedObject? parentNode = NetService.Root.GetNetObjectFromID(parentID);

				// Fallback to path;
				parentNode ??= NetService.Root.GetNetObj(parentPath);

				if (!NetService.IsPlaceReplicationDone)
				{
					// Check for deleted parents
					bool removed = false;
					foreach (string removedParentPath in _removedRef.ToArray())
					{
						if (removedParentPath != "" && parentPath.StartsWith(removedParentPath))
						{
							if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} Removed ref {removedParentPath}"); }
							_removedRef.Remove(removedParentPath);
							removed = true;
							break;
						}
					}

					if (removed)
					{
						if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] {data.NodePath} removed"); }
						CountInstanceLoaded(null);
						continue;
					}
				}

				if (parentNode != null)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] Chunk Replicate {data.NodePath}"); }
					parentNode?.RecvReplicate(data);
				}
				else
				{
					List<NetReplicateData> value = GetPendingReplicates(parentID);
					if (_useNetworkLog) { PT.Print($"[Net] [{NetService.LocalPeerID}] Pending {data.NodePath}"); }
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
				PT.Print(item.NodePath, " netID: ", item.NetworkID);
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
			if (netObj.GetType().IsDefined(typeof(NoSyncAttribute), false)) continue;

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

		if (netObj is Dynamic dyn)
		{
			NetService.TransformSync.SendUpdateTransform(dyn, true);
		}

		NetReplicateData netdata = netObj.GetNetReplicateData();
		netdata.Name = netObj.Name;
		netdata.IsSyncOnce = isSyncOnce;
		byte[] data = SerializeUtils.Serialize(netdata);

		Rpc(nameof(NetRecvReplicate), data);
	}

	public void SendNetReplicateRemove(NetworkedObject netobj, long peerID = -1)
	{
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
	private async void NetRecvReplicate(byte[] data)
	{
		// Ignore if from server itself
		if (NetService.IsServer) return;

		await Globals.Singleton.WaitFrame();

		NetReplicateData replicateData = SerializeUtils.Deserialize<NetReplicateData>(data)!;
		string parentID = replicateData.ParentNodeID;
		string parentNodePath = replicateData.ParentNodePath;

		if (_useNetworkLog) { PT.Print($"[Net] Recv Replicate {replicateData.NodePath}"); }

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
						if (_useNetworkLog) { PT.Print($"[Net] [@] Reparent {replicateData.NodePath}"); }

						existingChild.NetworkParent = parent;
					}
				}
			}
			else
			{
				if (_useNetworkLog) { PT.Print($"[Net] [+] Add {replicateData.NodePath}"); }
				parent.RecvReplicate(replicateData);
			}
		}
		else
		{
			// Parent not ready yet → queue
			List<NetReplicateData> value = GetPendingReplicates(parentID);
			if (_useNetworkLog) { PT.Print($"[Net] [?] Pending {replicateData.NodePath}"); }
			value.Add(replicateData);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvReplicateRemove(string nodePath)
	{
		if (NetService.IsServer) return;
		NetworkedObject? targetObj = NetService.Root.GetNetObjectFromID(nodePath);

		// Add removed reference if replicating
		if (!NetService.IsPlaceReplicationDone)
		{
			_removedRef.Add(nodePath);
		}

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
		_pendingReplicates.Clear();
		_removedRef.Clear();
		_worldReplicateSet.Clear();
		InstanceLoadedProgress = null;
		base.PreDelete();
	}
}
