// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Schemas.API;
using Polytoria.Utils;
using System.Threading.Tasks;

namespace Polytoria.Mobile.Utils;

public static class WorldsCache
{
	private static Task<APIWorldsRoot>? _request;

	public static Task<APIWorldsRoot> GetWorlds()
	{
		if (_request == null || _request.IsFaulted || _request.IsCanceled)
		{
			_request = PolyAPI.GetWorlds();
		}

		return _request;
	}

	public static void Clear()
	{
		_request = null;
	}
}
