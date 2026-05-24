// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Polytoria.Scripting.Luau;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int LuaContinuation(IntPtr L, int status);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LuaUserdataDestructor(IntPtr userData);

[StructLayout(LayoutKind.Sequential)]
public struct LuauCompileOptions
{
	internal int optimizationLevel;
	internal int debugLevel;
	internal int typeInfoLevel;
	internal int coverageLevel;

	internal IntPtr vectorLib;
	internal IntPtr vectorCtor;
	internal IntPtr vectorType;

	internal IntPtr mutableGlobals;
	internal IntPtr userdataTypes;

	public LuauCompileOptions()
	{
		optimizationLevel = 1;
		debugLevel = 1;
		typeInfoLevel = 0;
		coverageLevel = 0;
	}
}

internal partial class NativeBindings
{
#if GODOT_ANDROID
	private const string LuaLibraryName = "libLuau.VM.so";
	private const string CompilerLibraryName = "libLuau.Compiler.so";
#else
	private const string LuaLibraryName = "Luau.VM";
	private const string CompilerLibraryName = "Luau.Compiler";
#endif

	[LibraryImport(CompilerLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr luau_compile(string source, IntPtr size, IntPtr options, out IntPtr outsize);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int luau_load(IntPtr L, string name, byte[] bytecode, long size, int flags);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr luaL_newstate();

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_setsafeenv(IntPtr L, int index, int value);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_close(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void luaL_openlibs(IntPtr L);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int luaL_loadstring(IntPtr L, string s);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int luaL_loadfile(IntPtr L, string filename);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_pcall(IntPtr L, int nargs, int nresults, int errfunc);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr lua_tolstring(IntPtr L, int index, out IntPtr len);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_rawgeti(IntPtr L, int index, int n);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_rawseti(IntPtr luaState, int index, long i);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_gettop(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_settop(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_pushvalue(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_pushlstring(IntPtr luaState, byte[] s, UIntPtr len);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_insert(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_type(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr lua_typename(IntPtr L, int tp);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_isstring(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_isnumber(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_isboolean(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_iscfunction(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial double lua_tonumber(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_toboolean(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr lua_touserdata(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_pushnil(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_pushnumber(IntPtr L, double n);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_pushstring(IntPtr L, string s);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_pushboolean(IntPtr L, int b);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_pushcclosurek(IntPtr L, LuaFunction fn, string debugname, int nup, LuaContinuation? cont);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_pushlightuserdatatagged(IntPtr luaState, IntPtr p, int tag);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_newuserdatatagged(IntPtr L, UIntPtr size, int tag);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_newuserdatadtor(IntPtr L, UIntPtr sz, LuaUserdataDestructor? dtor);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr lua_newuserdata(IntPtr L, UIntPtr size);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_setfield(IntPtr L, int index, string k);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_getfield(IntPtr L, int index, string k);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_createtable(IntPtr L, int narr, int nrec);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_settable(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_gettable(IntPtr L, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_getmetatable(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial IntPtr lua_newthread(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial void lua_xmove(IntPtr from, IntPtr to, int n);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_resume(IntPtr L, int narg);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_resume(IntPtr L, IntPtr from, int narg);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_yield(IntPtr L, int nresults);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
	internal static partial int lua_status(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_setmetatable(IntPtr luaState, int objIndex);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_ref(IntPtr luaState, int idx);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_unref(IntPtr luaState, int reference);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int luaL_errorL(IntPtr luaState, string message);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_error(IntPtr luaState);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_newuserdatauv(IntPtr luaState, UIntPtr size, int nuvalue);

	[LibraryImport(LuaLibraryName, StringMarshalling = StringMarshalling.Utf8)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int luaL_newmetatable(IntPtr luaState, string name);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr luaL_tolstring(IntPtr luaState, int index, out IntPtr len);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_rawset(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_rawget(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_topointer(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_objlen(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_next(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_pushinteger(IntPtr luaState, long n);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial long lua_tointegerx(IntPtr luaState, int index, out int isNum);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial double lua_tonumberx(IntPtr L, int idx, out int isnum);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void luaL_unref(IntPtr luaState, int registryIndex, int reference);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_gc(IntPtr luaState, int what, int data);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_gc(IntPtr luaState, int what, int data, int data2);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_tothread(IntPtr luaState, int index);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_rawsetp(IntPtr luaState, int index, IntPtr p);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_rawgetp(IntPtr luaState, int index, IntPtr p);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_setfenv(IntPtr L, int idx);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_getfenv(IntPtr L, int idx);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_pushthread(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_debugtrace(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void luaL_sandbox(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void luaL_sandboxthread(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_replace(IntPtr L, int idx);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_remove(IntPtr L, int idx);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_getthreaddata(IntPtr L);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_setthreaddata(IntPtr L, IntPtr data);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_namecallatom(IntPtr L, out int len);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_newbuffer(IntPtr L, UIntPtr sz);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial IntPtr lua_tobuffer(IntPtr L, int idx, out UIntPtr len);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial int lua_checkstack(IntPtr L, int sz);

	[LibraryImport(LuaLibraryName)]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void lua_setreadonly(IntPtr L, int i, int b);
}
