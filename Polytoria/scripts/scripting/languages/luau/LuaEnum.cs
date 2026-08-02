// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Scripting.Luau;

public class LuaEnum : LuaMetatable
{
	public new Type TargetType = null!;

	public override int Index(IntPtr L)
	{
		return 0;
	}

	public override int NewIndex(IntPtr L)
	{
		return 0;
	}

	public override void RegisterMetamethods()
	{
		// Tostring
		int toStringFunc(IntPtr L)
		{
			LuaState state = LuaState.FromIntPtr(L);

			object? val = LangProvider.LuaToObject(state, 1);

			if (val is int i)
			{
				state.PushString(TargetType.Name + "." + (Enum.GetName(TargetType, i) ?? ""));
			}
			else
			{
				state.PushString(TargetType.Name);
			}

			return 1;
		}

		int safeToStringFunc(IntPtr L)
		{
			Exception? caughtException;

			try
			{
				return toStringFunc(L);
			}
			catch (Exception ex)
			{
				caughtException = ex;
			}

			if (caughtException != null)
			{
				LuaState state = LuaState.FromIntPtr(L);
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}

			return 0;
		}

		Lua.PushCFunction(safeToStringFunc, "__tostring");

		Lua.SetField(-2, "__tostring");

		// Equal Metamethod
		int eqFunc(IntPtr L)
		{
			LuaState state = LuaState.FromIntPtr(L);

			object? l = LangProvider.LuaToObject(state, -2);
			object? r = LangProvider.LuaToObject(state, -1);

			state.PushBoolean(Equals(l, r));

			return 1;
		}

		int safeEqFunc(IntPtr L)
		{
			Exception? caughtException;

			try
			{
				return eqFunc(L);
			}
			catch (Exception ex)
			{
				caughtException = ex;
			}

			if (caughtException != null)
			{
				LuaState state = LuaState.FromIntPtr(L);
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}

			return 0;
		}

		Lua.PushCFunction(safeEqFunc, "__eq");

		Lua.SetField(-2, "__eq");
	}
}
