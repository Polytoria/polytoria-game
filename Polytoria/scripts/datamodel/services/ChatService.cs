// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.WebAPI;
using Polytoria.Networking;
using Polytoria.Networking.RateLimiters;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Chat"), ExplorerExclude, SaveIgnore]
public sealed partial class ChatService : Instance
{
	private const int AllowedMessagePerWindow = 5;
	private const int AllowedMessageSecondsWindow = 5;
	private const int MaxMsgContentLength = 200;

	/// <summary>
	/// Fire when there's new chat message from player
	/// </summary>
	[ScriptProperty]
	public PTSignal<Player, string> NewChatMessage { get; private set; } = new();

	/// <summary>
	/// Fire when there's new message from broadcast/unicast
	/// </summary>
	[ScriptProperty]
	public PTSignal<string> MessageReceived { get; private set; } = new();

	/// <summary>
	/// Fire when the sent message is declined by the server
	/// </summary>
	[ScriptProperty]
	public PTSignal MessageDeclined { get; private set; } = new();

	/// <summary>
	/// Predicate function to determine if this message should be sent or not
	/// </summary>
	[ScriptProperty]
	public PTFunction? ChatPredicate { get; set; }

	private readonly PTHttpClient _client = new();

	private static readonly Dictionary<string, string> _builtInEmojis = [];
	public static IReadOnlyDictionary<string, string> BuiltInEmojis => _builtInEmojis;
	private const string EmojisPath = "res://assets/textures/client/emojis/";

	private readonly Dictionary<Player, SlidingWindowRateLimiter> _playerToRateLimiter = [];

	static ChatService()
	{
		if (!Globals.GDAvailable) return;
		foreach (string emojiFile in ResourceLoader.ListDirectory(EmojisPath))
		{
			string path = EmojisPath.PathJoin(emojiFile);
			if (ResourceLoader.Exists(path))
			{
				_builtInEmojis.Add(emojiFile[..^4], path);
			}
		}
	}

	public override void Ready()
	{
		Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);
		base.Ready();
	}

	private void OnPlayerRemoved(Player plr)
	{
		_playerToRateLimiter.Remove(plr);
	}

	public void SendChatMessage(string msgContent)
	{
		if (msgContent.Length > MaxMsgContentLength)
		{
			// Exceeded the maximum message content length
			NetMessageDeclined();
			BroadcastMessage($"[!] Your chat message is too long");
			return;
		}

		RpcId(1, nameof(NetServerRecvChatMessage), msgContent);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private async void NetServerRecvChatMessage(string msgContent)
	{
		int peerID = RemoteSenderId;
		Player? player = Root.Players.GetPlayerFromPeerID(peerID);

		// Check CanChat / Age restricted limitations
		if (player != null && (!player.CanChat || player.IsAgeRestricted))
		{
			RpcId(peerID, nameof(NetMessageDeclined));
			return;
		}

		// Filter message
		string filteredContent = FilterService.Filter(msgContent);

		if (player != null)
		{
			if (!player.IsAdmin)
			{
				// Escape BBCode
				filteredContent = filteredContent.Replace("[", "[lb]");
			}

			if (filteredContent.Length > MaxMsgContentLength)
			{
				// Exceeded the maximum message content length
				RpcId(peerID, nameof(NetMessageDeclined));
				UnicastMessage($"[!] Your chat message is too long", player);
				return;
			}

			if (!_playerToRateLimiter.TryGetValue(player, out var rateLimit))
			{
				_playerToRateLimiter[player] = new(AllowedMessagePerWindow, TimeSpan.FromSeconds(AllowedMessageSecondsWindow));
				rateLimit = _playerToRateLimiter[player];
			}

			if (!rateLimit.TryAccept())
			{
				// Rate limited
				RpcId(peerID, nameof(NetMessageDeclined));
				UnicastMessage($"[!] You need to cool off! Wait {AllowedMessageSecondsWindow} seconds before sending another message", player);
				return;
			}

			if (ChatPredicate != null)
			{
				// Handle ChatPredicate
				object?[] res = await ChatPredicate.Call(player, filteredContent);
				if (res.Length > 0 && res[0] is bool b && !b)
				{
					RpcId(peerID, nameof(NetMessageDeclined));
					return;
				}
			}

			// Log chat message
			_ = LogChatMessageAsync(player.UserID, filteredContent);

			PT.Print(player.Name, ": ", filteredContent);
			Rpc(nameof(NetRecvChatMessage), player.UserID, filteredContent);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, CallLocal = true, TransferChannel = 2)]
	private void NetRecvChatMessage(int userID, string msgContent)
	{
		Player? player = Root.Players.GetPlayerByID(userID);

		if (player != null)
		{
			NewChatMessage.Invoke(player, msgContent);
			player.InvokeChatted(msgContent);
		}
		else
		{
			PT.PrintWarn(userID, " not found in chat");
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private void NetMessageDeclined()
	{
		MessageDeclined.Invoke();
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private void NetRecvBroadcastMessage(string msgContent)
	{
		MessageReceived.Invoke(msgContent);
	}

	[ScriptMethod]
	public void BroadcastMessage(string msg)
	{
		MessageReceived.Invoke(msg);
		if (HasAuthority)
			Rpc(nameof(NetRecvBroadcastMessage), msg);
	}

	[ScriptMethod]
	public void UnicastMessage(string msg, Player plr)
	{
		if (plr == Root.Players.LocalPlayer)
		{
			MessageReceived.Invoke(msg);
		}
		else
		{
			RpcId(plr.PeerID, nameof(NetRecvBroadcastMessage), msg);
		}
	}

	[ScriptLegacyMethod("BroadcastMessage")]
	public void LegacyBroadcastMessage(string msg, object? _ = null)
	{
		BroadcastMessage(msg);
	}

	[ScriptLegacyMethod("UnicastMessage")]
	public void LegacyUnicastMessage(string msg, Player plr)
	{
		UnicastMessage(msg, plr);
	}

	private static readonly Regex _emojiRegex = new(@":([^:]+):", RegexOptions.Compiled);

	public static string FormatEmojis(string msg, float scale = 1f)
	{
		int size = Mathf.RoundToInt(24 * scale);
		return _emojiRegex.Replace(msg, match =>
		{
			string name = match.Groups[1].Value;
			if (_builtInEmojis.TryGetValue(name, out string? path))
				return $"[img={size}x{size}]{path}[/img]";
			return match.Value;
		});
	}

	// Logging for moderation
	private async Task LogChatMessageAsync(int userId, string message)
	{
		if (Root.IsLocalTest) return;

		Dictionary<string, string> form = new()
		{
			{ "userID", userId.ToString() },
			{ "message", message }
		};

		FormUrlEncodedContent content = new(form);

		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;

		try
		{
			await _client.PostAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/log"), content);
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Chat Logging Error: {ex.Message}");
		}
	}
}
