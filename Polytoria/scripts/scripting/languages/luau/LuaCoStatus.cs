// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Scripting.Luau;

public enum LuaCoStatus
{
	/// <summary>
	/// Running
	/// </summary>
	CoRun = 0,
	/// <summary>
	/// Suspended
	/// </summary>
	CoSus = 1,
	/// <summary>
	/// 'Normal' (resumed another coroutine)
	/// </summary>
	CoNor = 2,
	/// <summary>
	/// Finished
	/// </summary>
	CoFin = 3,
	/// <summary>
	/// Finished with error
	/// </summary>
	CoErr = 4
}
