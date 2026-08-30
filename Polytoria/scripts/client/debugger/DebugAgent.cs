// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Networking.Synchronizers;
using Polytoria.Schemas.Debugger;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Polytoria.Client.Debugger;

public class DebugAgent
{
	public bool ClientStarted { get; private set; } = false;
	private TcpClient _client = null!;
	private NetworkStream _stream = null!;
	private readonly List<KeyValuePair<string, TaskCompletionSource<MessageNewServerResponse>>> _pendingServerInstance = [];

	private string _address = "";

	public async Task Start(string addresss, int port, string? debugID = null)
	{
		if (ClientStarted) return;

		_address = addresss;
		_client = new TcpClient();

		await _client.ConnectAsync(addresss, port);

		_stream = _client.GetStream();

		_ = ReceiveMessages();

		ClientStarted = true;

		// Init messages
		int procId = Globals.IsMobileBuild ? 0 : OS.GetProcessId();

		if (debugID != null)
		{
			PT.Print("Reporting debug ID: ", debugID);
			await SendMessage(new MessageClientData() { DebugID = debugID, ProcessID = procId });
		}
		else
		{
			PT.Print("No debug ID attached");
			await SendMessage(new MessageClientData() { ProcessID = procId });
		}

		PT.PrintV($"-- Connected to debug server --");
	}

	private async Task ReceiveMessages()
	{
		while (true)
		{
			if (!_client.Connected) { ClientStarted = false; break; }
			byte[] buffer = new byte[1024];

			try
			{
				int bytesRead = await _stream.ReadAsync(buffer);

				if (bytesRead == 0)
				{
					PT.PrintV("Debug server closed connection");
					break;
				}

				IDebugMessage? msg = SerializeUtils.Deserialize<IDebugMessage>(buffer);
				if (msg != null)
				{
					OnMessageRecv(msg);
				}
			}
			catch (Exception e)
			{
				PT.PrintErrV(e);
				PT.PrintErrV($"Receive error: {e.Message}");
			}
		}
	}

	private void OnMessageRecv(IDebugMessage msg)
	{
		if (msg is MessageShutdown)
		{
			if (!Globals.IsMobileBuild)
			{
				Globals.Singleton.Quit();
			}
		}
		else if (Globals.IsMobileBuild)
		{
			if (msg is MessageLaunchWorld)
			{
				Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
				if (app is ClientEntry ce)
				{
					ClientEntry.ClientEntryData entryData = new()
					{
						ConnectAddress = _address,
						TestIsServer = false,
						TestUserID = 1144
					};
					ce.Entry(entryData);
				}
			}
		}
		else if (msg is MessageNewServerResponse ns)
		{
			foreach (var pair in _pendingServerInstance.ToArray())
			{
				if (pair.Key == ns.WorldPath)
				{
					pair.Value.SetResult(ns);
					_pendingServerInstance.Remove(pair);
				}
			}
		}
		else if (msg is MessageObjPropChange pc)
		{
			if (World.Current == null) return;
			if (!World.Current.Network.IsServer) return;
			NetworkedObject? obj = World.Current.GetObjectFromID(pc.ObjectID);
			if (obj != null)
			{
				PropSyncProp? prop = PropSyncRegistry.GetProp(obj.GetType(), pc.PropertyName);
				if (prop != null)
				{
					object? val = prop.Deserialize(pc.PropertyValue);

					// Call in main thread
					PT.CallOnMainThread(() =>
					{
						prop.SetValue(obj, val);
					});
				}
			}
		}
	}

	public async Task SendMessage(IDebugMessage msg)
	{
		if (!ClientStarted) return;
		byte[] data = SerializeUtils.Serialize(msg);
		try
		{
			await _stream.WriteAsync(data);
		}
		catch (Exception ex)
		{
			PT.PrintErrV(ex.Message);
		}
	}

	public async Task SendServerReady()
	{
		await SendMessage(new MessageServerReady());
	}

	public async Task SendLogDispatch(LogDispatcher.LogData data)
	{
		await SendMessage(new MessageLogDispatch()
		{
			LogType = data.LogType,
			LogFrom = data.LogFrom,
			Content = data.Content
		});
	}

	public async Task<MessageNewServerResponse> CreateServerInstance(string toPath)
	{
		TaskCompletionSource<MessageNewServerResponse> restsk = new();
		_pendingServerInstance.Add(new(toPath, restsk));
		await SendMessage(new MessageNewServerRequest() { WorldPath = toPath });
		return await restsk.Task;
	}
}
