// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.WebAPI;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Polytoria.Providers.Achievements;

public class PTAchievementsProvider : IAchievementsProvider
{
	private readonly PTHttpClient _client = new();

	public async Task RequestGiveAchievement(int userID, int achievementID)
	{
		SetHttpClientAuthToken();

		List<KeyValuePair<string, string>> formVariables =
		[
			new("userID", userID.ToString()),
			new("achievementID", achievementID.ToString()),
		];
		FormUrlEncodedContent formContent = new(formVariables);

		using var pa = await _client.PostAsync(
			Globals.ApiEndpoint.PathJoin("/v1/game/server/achievements/award"),
			formContent
		);
	}
	public async Task<bool> RequestHasAchievement(int userID, int achievementID)
	{
		SetHttpClientAuthToken();
		APIHasAchievementResponse res = await _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/game/server/achievements/has-achievement?userID=" + userID + "&achievementID=" + achievementID),
			ServerAPIGenerationContext.Default.APIHasAchievementResponse
		);
		return res.HasAchievement;
	}

	private void SetHttpClientAuthToken()
	{
		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;
	}
}
