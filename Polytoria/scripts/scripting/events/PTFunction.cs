// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting.Luau;
using System;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;
namespace Polytoria.Scripting;

public class PTFunction(Func<object?[], Task<object?[]>> target) : IScriptObject
{
	public Func<object?[], Task<object?[]>> _targetAction = target;
	public IScriptLanguageProvider LangProvider = null!;
	public Script? FromScript;

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
		LuauProvider provider = (LuauProvider)LangProvider;
		int top = state.GetTop();
		object?[] args = new object?[Math.Max(0, top - 1)];
		for (int i = 2; i <= top; i++)
		{
			args[i - 2] = provider.LuaToObject(state, i, getAsFunction: true);
		}
		TaskCompletionSource<int> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		LuauProvider.SetYieldTask(state, tcs.Task);

		_ = HandleCallAsync(provider, state, args, tcs);

		return state.Yield(1);
	}

	private async Task HandleCallAsync(LuauProvider provider, LuaState state, object?[] args, TaskCompletionSource<int> tcs)
	{
		try
		{
			Task<object?[]> call = Call(args);
			object?[] results = FromScript == null
				? await call
				: await call.WaitAsync(FromScript.LuauCancellation.Token);
			await Task.Yield();
			bool pushed = state.TryAccessYielded(() =>
			{
				foreach (object? item in results)
				{
					provider.PushValueToLua(state, item);
				}
			});
			tcs.TrySetResult(pushed ? results.Length : 0);
		}
		catch (Exception ex)
		{
			tcs.TrySetException(ex);
		}
	}
}
