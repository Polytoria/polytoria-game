// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Scripting;

/// <summary>
/// Cancellable handle returned by 'spawn' and 'delay' keyed on
/// the thread's native pointer.
/// </summary>
public class PTTask : IScriptObject
{
	internal Luau.LuaState Thread = null!;

	[ScriptMethod]
	public void Cancel() => Luau.LuauProvider.RequestCancel(Thread);
}
