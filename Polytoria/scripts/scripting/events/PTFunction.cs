// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting.Luau;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting;

public class PTFunction(Func<object?[], Task<object?[]>> target) : IScriptObject
{
	public Func<object?[], Task<object?[]>> _targetAction = target;
	public IScriptLanguageProvider LangProvider = null!;

	public async Task<object?[]> Call(params object?[]? args)
	{
		return await CallDirect(args ?? []);
	}

	public async Task<object?[]> CallDirect(object?[]? args)
	{
		return await _targetAction.Invoke(args ?? []);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Call), HandlesLuaState]
	public int LuaCall(LuaState state)
	{
		Script script = LuauProvider.GetScriptInstance(state);
		int top = state.GetTop();
		List<object?> argList = [];
		for (int i = 1; i <= top; i++)
		{
			argList.Add(LuauProvider.Singleton.LuaToObject(state, i + 1, getAsFunction: true));
		}
		object?[] args = [.. argList];
		TaskCompletionSource<int> tcs = new();
		LuauProvider.SetYieldTask(state, tcs.Task);

		_ = HandleCallAsync(state, args, tcs, script);

		return state.Yield(1);
	}

	private async Task HandleCallAsync(LuaState state, object?[] args, TaskCompletionSource<int> tcs, Script script)
	{
		try
		{
			object?[] results = await Call(args ?? []);
			if (!script.ShouldContinue)
			{
				tcs.TrySetResult(0);
				return;
			}
			foreach (object? item in results)
			{
				LuauProvider.Singleton.PushValueToLua(state, item);
			}
			tcs.TrySetResult(results.Length);
		}
		catch (Exception ex)
		{
			tcs.TrySetException(ex);
		}
	}
}
