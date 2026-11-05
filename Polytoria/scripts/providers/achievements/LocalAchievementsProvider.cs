// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Providers.Achievements;

public class LocalAchievementsProvider : IAchievementsProvider
{
	private readonly Dictionary<int, HashSet<int>> _achievements = [];

	public async Task RequestGiveAchievement(int userID, int achievementID)
	{
		if (!_achievements.TryGetValue(userID, out HashSet<int>? value))
		{
			value = [];
			_achievements.Add(userID, value);
		}

		value.Add(achievementID);
	}
	public async Task<bool> RequestHasAchievement(int userID, int achievementID)
	{
		_achievements.TryGetValue(userID, out HashSet<int>? value);
		return value != null && value.Contains(achievementID);
	}
}
