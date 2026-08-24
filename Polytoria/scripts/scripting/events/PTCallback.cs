// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting.Luau;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting;

public class PTCallback(Action<object?[]> target) : IDisposable, IScriptObject
{
	public Delegate? OriginalDelegate = null!;
	public Action<object?[]> TargetAction = target;
	public IScriptLanguageProvider LangProvider = null!;
	public Script? FromScript;
	private bool _disposed = false;
	public bool Disposed => _disposed;

	public void Invoke(params object?[] args)
	{
		if (_disposed) return;
		PT.CallOnMainThread(() =>
		{
			TargetAction.Invoke(args);
		});
	}

	public void InvokeDirect(object?[] args)
	{
		if (_disposed) return;
		PT.CallOnMainThread(() =>
		{
			TargetAction.Invoke(args);
		});
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Call), HandlesLuaState]
	public int LuaCall(LuaState state)
	{
		int top = state.GetTop();
		int argsCount = top;

		List<object?> argList = [];
		for (int i = 1; i <= top; i++)
		{
			argList.Add(LuauProvider.Singleton.LuaToObject(state, i + 1, getAsFunction: true));
		}
		object?[] args = [.. argList];

		TargetAction.Invoke(args ?? []);

		return 0;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		GC.SuppressFinalize(this);
	}
}
