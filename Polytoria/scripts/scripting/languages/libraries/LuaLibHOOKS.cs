// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Scripting.Libraries;

public class LuaLibHOOKS : IScriptObject
{
	public static List<PTFunction> Scheduled = [];

	[ScriptMethod("scheduleEvery")]
	public async static void ScheduleEvery(int milliseconds, PTFunction function)
	{
		Scheduled.Add(function);

		while (Scheduled.Contains(function))
		{
			await Task.Delay(milliseconds);
			await function.Call();
		}
	}

	[ScriptMethod("unschedule")]
	public static void Unschedule(PTFunction function)
	{
		Scheduled.Remove(function);
	}

	[ScriptMethod("scheduleIn")]
	public async static void ScheduleIn(int milliseconds, PTFunction function)
	{
		Scheduled.Add(function);
		await Task.Delay(milliseconds);
		await function.Call();
	}
}
