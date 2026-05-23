// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Polytoria.Scripting.Luau;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int LuaFunction(IntPtr state);

public partial class LuaState : IDisposable
{
	private IntPtr _state;
	private readonly LuaState? _parent;
	private bool _disposed;

	public const int LUA_MULTRET = -1;
	public const int LUAI_MAXCSTACK = 8000;
	public const int LUA_REGISTRYINDEX = -LUAI_MAXCSTACK - 2000;
	public const int LUA_ENVIRONINDEX = -LUAI_MAXCSTACK - 2001;
	public const int LUA_GLOBALSINDEX = -LUAI_MAXCSTACK - 2002;
	private static readonly Lock _lock = new();

	public IntPtr State => _state;
	public LuaState? Parent => _parent;
	public bool IsAlive => !_disposed && State != IntPtr.Zero && (Status() == LuaStatus.OK || Status() == LuaStatus.Yield);
	public bool Loaded { get; set; } = false;

	private static readonly Dictionary<LuaUserdataDestructor, GCHandle> _pinnedDestructors = [];
	private static readonly Dictionary<IntPtr, LuaFunction> _pinnedFunctions = [];

	public LuaState()
	{
		lock (_lock)
		{
			_state = NativeBindings.luaL_newstate();
			if (_state == IntPtr.Zero)
				throw new InvalidOperationException("Failed to create LuaState state");
		}
	}

	public LuaState(IntPtr state)
	{
		_state = state;
	}

	public static LuaState FromIntPtr(IntPtr state)
	{
		LuaState s = new(state);
		if (!s.IsAlive)
		{
			throw new LuaException("This state is dead");
		}
		return s;
	}

	public static byte[] Compile(string sourceCode)
	{
		/*
		IntPtr optionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(options));
		Marshal.StructureToPtr(options, optionsPtr, false);
		*/

		byte[] sourceBytes = Encoding.UTF8.GetBytes(sourceCode);
		IntPtr size = new(sourceBytes.Length);
		IntPtr bytecodePtr = NativeBindings.luau_compile(sourceCode, size, IntPtr.Zero, out nint outSize);
		if (bytecodePtr == IntPtr.Zero)
		{
			//Marshal.FreeHGlobal(optionsPtr);
			throw new Exception("Error compiling Lua source code");
		}
		byte[] bytecode = new byte[outSize.ToInt32()];
		Marshal.Copy(bytecodePtr, bytecode, 0, bytecode.Length);
		Marshal.FreeHGlobal(bytecodePtr);
		//Marshal.FreeHGlobal(optionsPtr);
		return bytecode;
	}

	public LuaStatus Load(string name, byte[] compiled)
	{
		lock (_lock)
		{
			if (Loaded)
				throw new Exception("Cannot load when already loaded! Please dispose this VM and create a new one");
			int loadResult = NativeBindings.luau_load(_state, name, compiled, compiled.Length, 0);
			if (loadResult != 0)
			{
				throw new LuaException("Error loading bytecode: " + ToString(-1));
			}
			Loaded = loadResult == 0;
			return (LuaStatus)loadResult;
		}
	}

	public void SetSafeEnv(int index, int value)
	{
		lock (_lock)
			NativeBindings.lua_setsafeenv(_state, index, value);
	}

	public LuaState(LuaState parent, IntPtr thread)
	{
		_state = thread;
		_parent = parent;
	}

	public void OpenLibs()
	{
		lock (_lock)
			NativeBindings.luaL_openlibs(_state);
	}

	/// <summary>
	/// Converts the acceptable index idx into an equivalent absolute index (that is, one that does not depend on the stack top). 
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	public int AbsIndex(int index)
	{
		if (index > 0 || index < LuaState.LUA_REGISTRYINDEX)
		{
			return index;
		}
		return GetTop() + index + 1;
	}

	/// <summary>
	/// Pushes an integer with value n onto the stack. 
	/// </summary>
	/// <param name="n"></param>
	public void PushInteger(long n)
	{
		lock (_lock)
			NativeBindings.lua_pushinteger(_state, n);
	}

	public bool Next(int index)
	{
		lock (_lock)
			return NativeBindings.lua_next(_state, index) != 0;
	}

	public int ObjLen(int idx)
	{
		lock (_lock)
			return (int)NativeBindings.lua_objlen(_state, idx);
	}

	public int RawGet(int index)
	{
		lock (_lock)
			return NativeBindings.lua_rawget(_state, index);
	}

	public void RawGetInteger(int index, int n)
	{
		lock (_lock)
			NativeBindings.lua_rawgeti(_state, index, n);
	}

	public void RawSetInteger(int index, long i)
	{
		lock (_lock)
			NativeBindings.lua_rawseti(_state, index, i);
	}

	public void RawSet(int index)
	{
		lock (_lock)
			NativeBindings.lua_rawset(_state, index);
	}

	public void Insert(int index)
	{
		lock (_lock)
			NativeBindings.lua_insert(_state, index);
	}

	public int Ref()
	{
		lock (_lock)
		{
			int refID = NativeBindings.lua_ref(_state, GetTop());
			Pop(1);
			return refID;
		}
	}

	public void GetRef(int reference)
	{
		RawGetInteger(LUA_REGISTRYINDEX, reference);
	}

	public void Unref(int reference)
	{
		lock (_lock)
			NativeBindings.lua_unref(_state, reference);
	}

	public int GarbageCollector(LuaGC what, int data)
	{
		lock (_lock)
			return NativeBindings.lua_gc(_state, (int)what, data);
	}

	public int GetTop()
	{
		lock (_lock)
			return NativeBindings.lua_gettop(_state);
	}

	public void SetTop(int index)
	{
		lock (_lock)
			NativeBindings.lua_settop(_state, index);
	}

	public void Pop(int n)
	{
		lock (_lock)
			NativeBindings.lua_settop(_state, -n - 1);
	}

	public void PushValue(int index)
	{
		lock (_lock)
			NativeBindings.lua_pushvalue(_state, index);
	}

	public void SetTable(int index)
	{
		lock (_lock)
			NativeBindings.lua_settable(_state, index);
	}

	public LuaType Type(int index)
	{
		lock (_lock)
			return (LuaType)NativeBindings.lua_type(_state, index);
	}

	public string TypeName(int index)
	{
		lock (_lock)
		{
			int type = NativeBindings.lua_type(_state, index);
			IntPtr namePtr = NativeBindings.lua_typename(_state, type);
			return Marshal.PtrToStringAnsi(namePtr) ?? "unknown";
		}
	}

	public bool IsString(int index) => Type(index) == LuaType.String;

	public bool IsNumber(int index) => Type(index) == LuaType.Number;

	public bool IsBoolean(int index) => Type(index) == LuaType.Boolean;

	public bool IsFunction(int index) => Type(index) == LuaType.Function;

	public bool IsNil(int index) => Type(index) == LuaType.Nil;

	public bool IsThread(int index) => Type(index) == LuaType.Thread;

	public bool IsBuffer(int index) => Type(index) == LuaType.Buffer;

	public string? ToString(int index, bool callMetamethod = false)
	{
		if (callMetamethod)
		{
			if (CallMetamethod(index, "__tostring"))
			{
				index = -1;
			}
		}
		lock (_lock)
		{
			IntPtr str = NativeBindings.lua_tolstring(_state, index, out IntPtr _);
			return str != IntPtr.Zero ? Marshal.PtrToStringUTF8(str) : null;
		}
	}

	public long ToInteger(int index)
	{
		lock (_lock)
			return NativeBindings.lua_tointegerx(_state, index, out int _);
	}

	/// <summary>
	/// Converts the LuaState value at the given index to the signed integral type lua_Integer. The LuaState value must be an integer, or a number or string convertible to an integer (see §3.4.3); otherwise, lua_tointegerx returns 0. 
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	public long? ToIntegerX(int index)
	{
		lock (_lock)
		{
			long value = NativeBindings.lua_tointegerx(_state, index, out int isInteger);
			if (isInteger != 0)
				return value;
			return null;
		}
	}

	/// <summary>
	/// Call metamethod of current metatable
	/// </summary>
	/// <param name="obj"></param>
	/// <param name="name"></param>
	/// <returns></returns>

	public bool CallMetamethod(int obj, string name)
	{
		lock (_lock)
		{
			if (obj < 0)
			{
				obj = GetTop() + obj + 1;
			}

			if (GetMetaTable(obj) == LuaType.Nil)
			{
				return false;
			}

			PushString(name);
			RawGet(-2);

			if (!IsFunction(-1))
			{
				Pop(2);
				return false;
			}

			PushValue(obj);

			try
			{
				Call(1, 1);
			}
			catch (Exception ex)
			{
				GD.PushError(ex.InnerException ?? ex);
				throw;
			}

			return true;
		}
	}

	public double ToNumber(int index)
	{
		lock (_lock)
			return NativeBindings.lua_tonumberx(_state, index, out _);
	}

	public bool ToBoolean(int index)
	{
		lock (_lock)
			return NativeBindings.lua_toboolean(_state, index) != 0;
	}

	public LuaState ToThread(int index)
	{
		IntPtr state = NativeBindings.lua_tothread(_state, index);
		if (state == _state)
			return this;

		return FromIntPtr(state);
	}

	public IntPtr ToUserData(int index)
	{
		lock (_lock)
			return NativeBindings.lua_touserdata(_state, index);
	}

	public IntPtr ToPointer(int index)
	{
		lock (_lock)
			return NativeBindings.lua_topointer(_state, index);
	}

	public bool IsLightUserData(int index) => Type(index) == LuaType.LightUserData;

	public bool IsUserData(int index) => Type(index) == LuaType.UserData;

	public bool IsTable(int index) => Type(index) == LuaType.Table;

	public void PushNil()
	{
		lock (_lock)
			NativeBindings.lua_pushnil(_state);
	}

	public void PushNumber(double n)
	{
		lock (_lock)
			NativeBindings.lua_pushnumber(_state, n);
	}

	public void PushString(string s)
	{
		lock (_lock)
			NativeBindings.lua_pushstring(_state, s);
	}

	public void PushBoolean(bool b)
	{
		lock (_lock)
			NativeBindings.lua_pushboolean(_state, b ? 1 : 0);
	}

	public void PushCFunction(LuaFunction func, string name = "luafunc", int n = 0)
	{
		IntPtr userdataPtr = NewUserDataDTor((UIntPtr)IntPtr.Size, FunctionGarbageCollect);

		GCHandle handle = GCHandle.Alloc(func);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);

		_pinnedFunctions[handlePtr] = func;

		lock (_lock)
			NativeBindings.lua_pushcclosurek(_state, func, name, n + 1, null);
	}

	private static void FunctionGarbageCollect(IntPtr ud)
	{
		IntPtr handlePtr = Marshal.ReadIntPtr(ud);
		if (handlePtr == IntPtr.Zero) return;
		GCHandle handle = GCHandle.FromIntPtr(handlePtr);
		_pinnedFunctions.Remove(handlePtr);

		if (handle.IsAllocated)
		{
			handle.Free();
		}
	}

	public bool PushThread()
	{
		lock (_lock)
			return NativeBindings.lua_pushthread(_state) == 1;
	}

	public byte[] ToBuffer(int index)
	{
		lock (_lock)
		{
			IntPtr ptr = NativeBindings.lua_tobuffer(_state, index, out UIntPtr length);

			if (ptr == IntPtr.Zero)
			{
				return [];
			}

			int len = (int)length;
			byte[] managedArray = new byte[len];

			Marshal.Copy(ptr, managedArray, 0, len);

			return managedArray;
		}
	}

	public void SetGlobal(string name)
	{
		SetField(LUA_GLOBALSINDEX, name);
	}

	public void GetGlobal(string name)
	{
		lock (_lock)
			NativeBindings.lua_getfield(_state, LUA_GLOBALSINDEX, name);
	}

	public void GetTable(int index)
	{
		lock (_lock)
			NativeBindings.lua_gettable(_state, index);
	}

	public LuaType GetMetaTable(int index)
	{
		lock (_lock)
			return (LuaType)NativeBindings.lua_getmetatable(_state, index);
	}

	public void Register(string name, LuaFunction func)
	{
		PushCFunction(func, name);
		SetGlobal(name);
	}

	public void NewTable()
	{
		lock (_lock)
			NativeBindings.lua_createtable(_state, 0, 0);
	}

	public void SetField(int index, string key)
	{
		lock (_lock)
			NativeBindings.lua_setfield(_state, index, key);
	}

	public void GetField(int index, string key)
	{
		lock (_lock)
			NativeBindings.lua_getfield(_state, index, key);
	}

	public void PushLightUserData(IntPtr ptr)
	{
		lock (_lock)
			NativeBindings.lua_pushlightuserdatatagged(_state, ptr, 0);
	}

	public void PushObject<T>(T obj)
	{
		if (obj == null)
		{
			PushNil();
			return;
		}

		GCHandle handle = GCHandle.Alloc(obj);
		PushLightUserData(GCHandle.ToIntPtr(handle));
	}

	public object? ToObject(int index)
	{
		if (IsNil(index) || !IsLightUserData(index))
			return null;

		IntPtr data = ToUserData(index);
		if (data == IntPtr.Zero)
			return null;

		var handle = GCHandle.FromIntPtr(data);
		if (!handle.IsAllocated)
			return null;

		return handle.Target;
	}

	public T? ToObject<T>(int index, bool freeGCHandle = true)
	{
		if (IsNil(index) || !IsLightUserData(index))
			return default;

		IntPtr data = ToUserData(index);
		if (data == IntPtr.Zero)
			return default;

		var handle = GCHandle.FromIntPtr(data);
		if (!handle.IsAllocated)
			return default;

		T? reference = (T?)handle.Target;

		if (freeGCHandle)
			handle.Free();

		return reference;
	}

	public void PushBuffer(byte[] buffer)
	{
		if (buffer == null)
		{
			PushNil();
			return;
		}

		lock (_lock)
		{
			IntPtr bufferPtr = NativeBindings.lua_newbuffer(_state, (UIntPtr)buffer.Length);

			if (bufferPtr == IntPtr.Zero)
			{
				throw new OutOfMemoryException("Lua failed to allocate the buffer.");
			}

			Marshal.Copy(buffer, 0, bufferPtr, buffer.Length);
		}
	}

	public void SetMetaTable(int objIndex)
	{
		lock (_lock)
			NativeBindings.lua_setmetatable(_state, objIndex);
	}

	public LuaState NewThread()
	{
		lock (_lock)
		{
			if (NativeBindings.lua_checkstack(_state, 1) == 0)
				throw new InvalidOperationException("Lua stack limit: unable to create new thread");

			IntPtr thread = NativeBindings.lua_newthread(_state);
			return new LuaState(this, thread);
		}
	}

	public void Sandbox()
	{
		lock (_lock)
			NativeBindings.luaL_sandbox(_state);
	}

	public void SandboxGlobals()
	{
		lock (_lock)
		{
			NewTable();

			NewTable();
			PushValue(LuaState.LUA_GLOBALSINDEX);
			SetField(-2, "__index");
			SetReadOnly(-1, true);
			SetMetaTable(-2);

			Replace(LuaState.LUA_GLOBALSINDEX);
		}
	}

	public string? GetNameCallAtom()
	{
		lock (_lock)
		{
			IntPtr ptr = NativeBindings.lua_namecallatom(_state, out int len);
			if (ptr == IntPtr.Zero) return null;
			if (len < 0) { return Marshal.PtrToStringAnsi(ptr); }
			return Marshal.PtrToStringAnsi(ptr, len);
		}
	}

	public void XMove(LuaState to, int n)
	{
		lock (_lock)
			NativeBindings.lua_xmove(_state, to.State, n);
	}

	public LuaStatus Resume(LuaState? from = null, int narg = 0)
	{
		lock (_lock)
			return (LuaStatus)NativeBindings.lua_resume(_state, from?.State ?? IntPtr.Zero, narg);
	}

	public LuaStatus Status()
	{
		lock (_lock)
			return (LuaStatus)NativeBindings.lua_status(_state);
	}

	public int Yield(int nresults)
	{
		lock (_lock)
			return NativeBindings.lua_yield(_state, nresults);
	}

	public int Error(string value, params object[] v)
	{
		string message = v.Length > 0 ? string.Format(value, v) : value;
		lock (_lock)
			return NativeBindings.luaL_errorL(_state, message);
	}

	public void PushLightUserdataTagged(IntPtr p, int tag = 0)
	{
		lock (_lock)
			NativeBindings.lua_pushlightuserdatatagged(_state, p, tag);
	}

	public IntPtr NewUserDataTagged(UIntPtr size, int tag)
	{
		lock (_lock)
			return NativeBindings.lua_newuserdatatagged(_state, size, tag);
	}

	public IntPtr NewUserDataDTor(UIntPtr size, LuaUserdataDestructor dtor)
	{
		lock (_lock)
		{
			if (!_pinnedDestructors.ContainsKey(dtor))
			{
				_pinnedDestructors[dtor] = GCHandle.Alloc(dtor);
			}
			return NativeBindings.lua_newuserdatadtor(_state, size, dtor);
		}
	}

	public bool NewMetaTable(string name)
	{
		lock (_lock)
			return NativeBindings.luaL_newmetatable(_state, name) != 0;
	}

	public string? DebugTrace()
	{
		lock (_lock)
		{
			IntPtr ptr = NativeBindings.lua_debugtrace(_state);
			return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : null;
		}
	}

	/// <summary>
	/// Does the equivalent of t[p] = v, where t is the table at the given index, p is encoded as a light userdata, and v is the value at the top of the stack. 
	/// </summary>
	/// <param name="index"></param>
	/// <param name="obj"></param>
	public void RawSetPointer(int index, IntPtr obj)
	{
		lock (_lock)
			NativeBindings.lua_rawsetp(_state, index, obj);
	}

	/// <summary>
	/// Pushes onto the stack the value t[k], where t is the table at the given index and k is the pointer p represented as a light userdata. The access is raw; that is, it does not invoke the __index metamethod. 
	/// </summary>
	/// <param name="index"></param>
	/// <param name="obj"></param>
	/// <returns>Returns the type of the pushed value. </returns>
	public LuaType RawGetPointer(int index, IntPtr obj)
	{
		lock (_lock)
			return (LuaType)NativeBindings.lua_rawgetp(_state, index, obj);
	}

	public void SetThreadData(IntPtr data)
	{
		lock (_lock)
			NativeBindings.lua_setthreaddata(_state, data);
	}

	public IntPtr GetThreadData()
	{
		lock (_lock)
			return NativeBindings.lua_getthreaddata(_state);
	}

	public void Getfenv(int idx)
	{
		lock (_lock)
			NativeBindings.lua_getfenv(_state, idx);
	}

	public int Setfenv(int idx)
	{
		lock (_lock)
			return NativeBindings.lua_setfenv(_state, idx);
	}

	public void Replace(int idx)
	{
		lock (_lock)
			NativeBindings.lua_replace(_state, idx);
	}

	public void Remove(int idx)
	{
		lock (_lock)
			NativeBindings.lua_remove(_state, idx);
	}

	public void SetReadOnly(int idx, bool to)
	{
		lock (_lock)
			NativeBindings.lua_setreadonly(_state, idx, to ? 1 : 0);
	}

	public void Call(int nargs, int nresults)
	{
		lock (_lock)
		{
			if (NativeBindings.lua_pcall(_state, nargs, nresults, 0) != (int)LuaStatus.OK)
			{
				string? error = ToString(-1);
				Pop(1);
				throw new LuaException($"Call error: {error}");
			}
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			if (!_disposed && _state != IntPtr.Zero)
			{
				NativeBindings.lua_close(_state);
				_state = IntPtr.Zero;
				_disposed = true;
			}
		}
		GC.SuppressFinalize(this);
	}

	public class LuaException(string message) : Exception(message)
	{
	}
}
