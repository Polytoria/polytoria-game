// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.WebAPI;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Social")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class SocialService : Instance
{
	private readonly PTHttpClient _client = new();
	public readonly Dictionary<string, FileLinkAsset> FileLinks = [];

	public void LocalSendFriendshipRequest(Player recipient, FriendshipRequestType req)
	{
		RpcId(1, nameof(NetRecvFriendshipRequest), recipient.UserID, (int)req);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private async void NetRecvFriendshipRequest(int recipientID, int req)
	{
		FriendshipRequestType reqType = (FriendshipRequestType)req;
		Player? from = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		Player? to = Root.Players.GetPlayerByID(recipientID);

		if (from != null && to != null)
		{
			try
			{
				await WebSendFriendshipRequest(from.UserID, to.UserID, reqType);
				if (reqType == FriendshipRequestType.Friend)
				{
					RpcId(from.PeerID, nameof(RecvFriendRequestSuccess), to.UserID);
					RpcId(to.PeerID, nameof(RecvFriendRequestNotify), from.UserID);
				}
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
				RpcId(from.PeerID, nameof(RecvFriendRequestFailure));
			}
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestSuccess(int toUserID)
	{
		Player? to = Root.Players.GetPlayerByID(toUserID);
		if (to != null)
		{
			Root.CoreUI.CoreUI.NotificationCenter.FireMessage("You just sent a friend request to " + to.Name, "Friend Request Sent!");
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestFailure()
	{
		Root.CoreUI.CoreUI.NotificationCenter.FireMessage("Something went wrong, please try again.", "Cannot send friend request");
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private async void RecvFriendRequestNotify(int fromUserID)
	{
		Player? from = Root.Players.GetPlayerByID(fromUserID);
		if (from != null)
		{
			Root.CoreUI.CoreUI.NotificationCenter.FireMessage(from.Name + " just send you a friend request!", "Friend Request");
		}
	}

	public async Task WebSendFriendshipRequest(int senderID, int recipientID, FriendshipRequestType req)
	{
		string data = JsonSerializer.Serialize(new()
		{
			UserID = senderID,
			FriendID = recipientID
		}, SocialAPIGenerationContext.Default.APIFriendRequest);

		string url = req switch
		{
			FriendshipRequestType.Friend => Globals.ApiEndpoint.PathJoin("/v1/game/server/friends/request"),
			FriendshipRequestType.Unfriend => Globals.ApiEndpoint.PathJoin("/v1/game/server/friends/remove"),
			_ => throw new NotSupportedException("Unsupported relationship type"),
		};

		HttpRequestMessage msg = new(HttpMethod.Post, url);
		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;
		msg.Content = new StringContent(data, new MediaTypeHeaderValue("application/json"));
		await _client.SendAsync(msg);
	}

	public async Task<bool> WebCheckAreFriends(int fromID, int toID)
	{
		return (await _client.GetFromJsonAsync(Globals.ApiEndpoint.PathJoin($"/v1/users/{fromID}/friends/{toID}"), SocialAPIGenerationContext.Default.APIAreFriendsResponse)).AreFriends;
	}
	
	public async Task<APIAreFriendsResponse?> WebCheckFriendsStatus(int fromID, int toID)
	{
		return await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/users/{fromID}/friends/{toID}"),
			SocialAPIGenerationContext.Default.APIAreFriendsResponse
		);
	}

	public enum FriendshipRequestType
	{
		Friend,
		Unfriend,
		Block
	}
}
