// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Creator.UI;
#endif
using Polytoria.Datamodel;
using Polytoria.Networking;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Scripting;

[Internal, NoSync]
public partial class LogDispatcher : NetworkedObject
{
	private const int MaxLogLength = 16384;
	public event Action<LogData>? NewLog;
	public event Action<LogData[]>? LogSynchronized;
	public List<LogData> ServerLogs = [];
	public List<LogData> Logs = [];

	public override void Init()
	{
		base.Init();
		if (Root != null)
		{
			if (Root.IsLoaded)
			{
				OnGameReady();
			}
			else
			{
				Root.Loaded.Once(OnGameReady);
			}
		}
	}

	private void OnGameReady()
	{
		if (!Root.Network.IsServer && Root.SessionType != World.SessionTypeEnum.Creator)
		{
			RpcId(1, nameof(NetReqServerLogs));
		}
	}

	public void LogInfo(Datamodel.Script from, string content)
	{
		PT.PrintV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Info,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	public void LogWarning(Datamodel.Script from, string content)
	{
		PT.PrintV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Warning,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	public void LogError(Datamodel.Script from, string content)
	{
		PT.PrintErrV($"[Lua] {from.NetworkPath} {content}");
		DispatchLog(new()
		{
			ID = Guid.NewGuid().ToString(),
			LogType = LogTypeEnum.Error,
			Content = content,
			LogFrom = (from is ClientScript) ? LogFromEnum.Client : LogFromEnum.Server
		});
	}

	internal async void DispatchLog(LogData data)
	{
		if (Root.Network.IsServer && Root.SessionType == World.SessionTypeEnum.Client)
		{
			// Explicitly set on server if is client/ from server
			data.LogFrom = LogFromEnum.Server;
		}
		data.LoggedAt = DateTime.UtcNow;
		PT.CallOnMainThread(() =>
		{
			InvokeNewLog(data);
			if (Root.Network.IsServer)
			{
				foreach (Player plr in Root.Players.GetPlayers())
				{
					// If is creator or in beta program (beta gets the logs), or is solo test
					if (plr.IsCreator || plr.IsAdmin || Globals.IsBetaBuild || (Root.Entry != null && Root.Entry.IsSoloTest))
					{
						RpcId(plr.PeerID, nameof(NetRecvLog), SerializeUtils.Serialize(data));
					}
				}
			}
			// TODO: Turn this into an event instead? Maybe dispatch it to PT
#if CREATOR
			DebugConsole.Singleton?.NewLog(data);
#endif
		});
		if (Root.Entry?.DebugAgent != null)
		{
			await Root.Entry.DebugAgent.SendLogDispatch(data);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvLog(byte[] rawdata)
	{
		LogData? data = SerializeUtils.Deserialize<LogData>(rawdata);
		if (data != null)
		{
			InvokeNewLog(data);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqServerLogs()
	{
		RpcId(RemoteSenderId, nameof(NetRecvServerLogs), SerializeUtils.Serialize(Logs.ToArray()));
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvServerLogs(byte[] rawdata)
	{
		LogData[]? data = SerializeUtils.Deserialize<LogData[]>(rawdata);
		if (data != null)
		{
			foreach (LogData item in data)
			{
				RegisterLogItem(item);
			}

			LogSynchronized?.Invoke([.. Logs]);
		}
	}

	private void RegisterLogItem(LogData item)
	{
		// Clear loggedAt data if from server and receiver is client (time from sserver may be desynchronized with the client)
		if (item.LogFrom == LogFromEnum.Server && Root.SessionType == World.SessionTypeEnum.Client && Root.Network.IsProd)
		{
			item.LoggedAt = DateTime.UtcNow;
		}
		Logs.Add(item);
		if (Logs.Count > MaxLogLength)
		{
			Logs.RemoveAt(0);
		}
		if (ServerLogs.Count > MaxLogLength)
		{
			ServerLogs.RemoveAt(0);
		}
	}

	private void InvokeNewLog(LogData item)
	{
		RegisterLogItem(item);
		NewLog?.Invoke(item);
	}

	[MemoryPackable]
	public partial class LogData
	{
		public LogTypeEnum LogType;
		public LogFromEnum LogFrom = LogFromEnum.None;
		public string ID = "";
		public string Content = "";
		public DateTime LoggedAt;

		public override int GetHashCode()
		{
			return ID.GetHashCode();
		}
	}

	public enum LogTypeEnum
	{
		Info,
		Error,
		Warning
	}

	public enum LogFromEnum
	{
		None,
		Client,
		Server,
		Addon
	}
}
