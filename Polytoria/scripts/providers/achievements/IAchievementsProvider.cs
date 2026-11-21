// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;

namespace Polytoria.Providers.Achievements;

public interface IAchievementsProvider
{
	Task RequestGiveAchievement(int userID, int achievementID);
	Task<bool> RequestHasAchievement(int userID, int achievementID);
}
