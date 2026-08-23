// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using Polytoria.Utils.Compression;
using System;
using System.Collections.Generic;
using System.Text.Json;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Networking.Synchronizers;

[Internal]
public partial class NetworkScriptSync : Instance
{
	internal NetworkService NetService = null!;

	private static readonly bool _useNetworkLog = false;

	static NetworkScriptSync()
	{
		if (Globals.IsInGDEditor) return;
		_useNetworkLog = OS.HasFeature("netlog");
	}

	public void SyncScriptsToPeer(int peerID)
	{
		NetworkedObject[] allNetObjs = NetService.Root.GetReplicateDescendants();
		List<NetBatchScriptData> data = [];

		foreach (NetworkedObject item in allNetObjs)
		{
			if (item is ClientScript || item is ModuleScript)
			{
				if (item is Datamodel.Script cs)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [ScriptSync] Packing {cs.Name} source"); }
					try
					{
						cs.TryCompile();
					}
					catch (Exception ex)
					{
						RpcId(peerID, nameof(NetLogCompileError), ex.Message);
						continue;
					}
					data.Add(new() { NetID = cs.NetworkedObjectID, Bytecode = cs.Bytecode! });
				}
			}
		}

		byte[] rawData = ZstdCompressionUtils.Compress(JsonSerializer.Serialize([.. data], NetDataGenerationContext.Default.NetBatchScriptDataArray).ToUtf8Buffer());
		RpcId(peerID, nameof(NetRecvAllScripts), rawData, true);
	}


	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvAllScripts(byte[] rawBytes, bool isFirstInit)
	{
		NetBatchScriptData[] scriptsData = JsonSerializer.Deserialize(ZstdCompressionUtils.Decompress(rawBytes), NetDataGenerationContext.Default.NetBatchScriptDataArray)!;

		foreach (NetBatchScriptData item in scriptsData)
		{
			NetworkedObject? obj = NetService.Root.GetNetObjectFromID(item.NetID);

			if (obj != null && (obj is ClientScript || obj is ModuleScript))
			{
				if (obj is Datamodel.Script s)
				{
					if (_useNetworkLog) { PT.Print($"[Net] [ScriptSync] Recv {s.Name} source"); }
					s.Bytecode = item.Bytecode;
					if (s is ModuleScript ms) ms.NotifyBytecodeReceived();
				}
			}
			else
			{
				GD.PushWarning("[ScriptSync] ", item.NetID, " not found");
			}
		}

		if (isFirstInit)
		{
			NetService.NetScriptSyncd();
		}
	}

	public void RequestSource(Datamodel.Script script)
	{
		RpcId(1, nameof(NetReqSource), script.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqSource(string netID)
	{
		int r = RemoteSenderId;
		NetworkedObject? obj = NetService.Root.GetNetObjectFromID(netID);
		if (obj != null && (obj is ClientScript || obj is ModuleScript))
		{
			if (obj is Datamodel.Script script)
			{
				try
				{
					script.TryCompile();
				}
				catch (Exception ex)
				{
					RpcId(r, nameof(NetLogCompileError), ex.Message);
					return;
				}
				RpcId(r, nameof(NetRecvSource), netID, script.Bytecode);
			}
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvSource(string netID, byte[] byteCode)
	{
		NetworkedObject? obj = NetService.Root.GetNetObjectFromID(netID);
		if (obj is ClientScript script)
		{
			script.Bytecode = byteCode;
			script.TryRun();
		}
		else if (obj is ModuleScript ms)
		{
			ms.Bytecode = byteCode;
			ms.NotifyBytecodeReceived();
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetLogCompileError(string msg)
	{
		NetService.Root.ScriptService.Logger.DispatchLog(new() { Content = msg, LogType = Scripting.LogDispatcher.LogTypeEnum.Error });
	}
}
