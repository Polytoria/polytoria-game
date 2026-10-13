// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Scripting.Luau;
/// <summary>
/// Garbage Collector operations
/// </summary>
public enum LuaGC
{
	/// <summary>
	///  Stops the garbage collector. 
	/// </summary>
	Stop = 0,
	/// <summary>
	/// Restarts the garbage collector. 
	/// </summary>
	Restart = 1,
	/// <summary>
	/// Performs a full garbage-collection cycle. 
	/// </summary>
	Collect = 2,
	/// <summary>
	///  Returns the current amount of memory (in Kbytes) in use by Lua. 
	/// </summary>
	Count = 3,
	/// <summary>
	///  Returns the remainder of dividing the current amount of bytes of memory in use by Lua by 1024
	/// </summary>
	Countb = 4,
	/// <summary>
	///  Performs an incremental step of garbage collection. 
	/// </summary>
	IsRunning = 5,
	/// <summary>
	/// Performs an incremental step of garbage collection.
	/// </summary>
	Step = 6,
	/// <summary>
	/// Sets the target heap size as a percentage of live data.
	/// </summary>
	SetGoal = 7,
	/// <summary>
	/// Sets the garbage collection work multiplier.
	/// </summary>
	SetStepMultiplier = 8,
	/// <summary>
	/// Sets the garbage collector step size.
	/// </summary>
	SetStepSize = 9,
}
