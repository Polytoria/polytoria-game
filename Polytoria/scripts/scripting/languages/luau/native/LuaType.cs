// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Scripting.Luau;

/// <summary>
/// Lua types
/// </summary>
public enum LuaType
{
	Nil = 0,
	Boolean = 1,
	LightUserData = 2,
	Number = 3,
	Integer = 4,
	Vector = 5,
	String = 6,
	Table = 7,
	Function = 8,
	UserData = 9,
	Thread = 10,
	Buffer = 11,
	Proto = 12,
	UpVal = 13,
	DeadKey = 14,
	Count = Proto // = 12
}
