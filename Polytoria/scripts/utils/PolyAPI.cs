// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Polytoria.Utils;

public static class PolyAPI
{
	private static readonly PTHttpClient _client = new();

	public static void SetAuthToken(string userToken)
	{
		// Remove Authorization if exists
		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Add("Authorization", "Bearer " + userToken);
	}

	public static Task<APIUserInfo> GetUserFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString()),
			APIGenerationContext.Default.APIUserInfo
		);
	}

	public static Task<APIMeResponse> GetCurrentUser()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/users/me"),
			APIGenerationContext.Default.APIMeResponse
		);
	}

	public static async Task<APIJoinPlaceResponse> RequestJoinGame(APIJoinPlaceRequest req)
	{
		HttpResponseMessage response = await _client.PostAsJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/places/join"),
			req,
			APIGenerationContext.Default.APIJoinPlaceRequest
		);

		response.EnsureSuccessStatusCode();

		APIJoinPlaceResponse result = await response.Content.ReadFromJsonAsync(
			APIGenerationContext.Default.APIJoinPlaceResponse
		);

		return result;
	}

	public static Task<APIAvatarResponse> GetUserAvatarFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString() + "/avatar"),
			APIGenerationContext.Default.APIAvatarResponse
		);
	}

	public static Task<APIPlaceInfo> GetWorldFromID(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString()),
			APIGenerationContext.Default.APIPlaceInfo
		);
	}

	public static Task<APIPlaceMedia[]?> GetWorldMedia(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString() + "/media"),
			APIGenerationContext.Default.APIPlaceMediaArray
		);
	}

	public static Task<APIFeedPostRoot> GetFeedPosts(int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/feed?page=" + page.ToString()),
			APIGenerationContext.Default.APIFeedPostRoot
		);
	}

	public static Task<APIWorldsRoot> GetWorlds()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/places"),
			APIGenerationContext.Default.APIWorldsRoot
		);
	}

	public static Task<APIStoreItem> GetStoreItem(int id)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/store/" + id),
			APIGenerationContext.Default.APIStoreItem
		);
	}

#if CREATOR
	public static Task<APILibraryResponse> GetLibrary(LibraryQueryTypeEnum type, int page = 1, string searchQuery = "")
	{
		string queryType = type switch
		{
			LibraryQueryTypeEnum.Model => "model",
			LibraryQueryTypeEnum.Image => "decal",
			LibraryQueryTypeEnum.Audio => "audio",
			LibraryQueryTypeEnum.Mesh => "mesh",
			LibraryQueryTypeEnum.Addon => "addon",
			_ => ""
		};
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin($"/api/library?page={page}&search={searchQuery}&type={queryType}"),
			APIGenerationContext.Default.APILibraryResponse
		);
	}
#endif

	public static Task<string> GetProfanityList()
	{
		return _client.GetStringAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/profanity"));
	}
}
