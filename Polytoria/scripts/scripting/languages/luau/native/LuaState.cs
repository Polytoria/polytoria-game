// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
	private readonly bool _ownsState;
	private bool _disposed;

	public const int LUA_MULTRET = -1;
	public const int LUAI_MAXCSTACK = 8000;
	public const int LUA_REGISTRYINDEX = -LUAI_MAXCSTACK - 2000;
	public const int LUA_ENVIRONINDEX = -LUAI_MAXCSTACK - 2001;
	public const int LUA_GLOBALSINDEX = -LUAI_MAXCSTACK - 2002;
	private const double ScriptTimeoutSec = 20;
	private const long HeapLimit = 256L << 20;

	private static readonly IntPtr _vectorLib = Marshal.StringToCoTaskMemUTF8("Vector3");
	private static readonly IntPtr _vectorCtor = Marshal.StringToCoTaskMemUTF8("New");

	public IntPtr State => _state;
	public LuaState? Parent => _parent;
	public bool IsAlive => !_disposed && State != IntPtr.Zero && Status() is LuaStatus.OK or LuaStatus.Yield;
	public bool Loaded { get; set; } = false;

	private static readonly Dictionary<LuaUserdataDestructor, GCHandle> _pinnedDestructors = [];
	private static readonly LuaUserdataDestructor _functionDtor = FunctionGarbageCollect;
	private static readonly LuaUserdataDestructor _objectDtor = ManagedObjectGarbageCollect;

	private static LuaState? _lastFromIntPtr;

	private static string[] _atoms = [];
	private static Dictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> _atomLookup;

	private static readonly Lock _watchdogLock = new();
	private static readonly LuaInterrupt _interrupt = Interrupt;
	private static readonly IntPtr _interruptPtr = Marshal.GetFunctionPointerForDelegate(_interrupt);
	private static System.Threading.Timer? _watchdog;
	private static int _resumeDepth;
	private static long _resumeStart;
	private static IntPtr _resumeCallbacks;
	private static long _heapSize;
	private static long _lastCollect;

	public unsafe LuaState()
	{
		_ownsState = true;
		_state = NativeBindings.lua_newstate((IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&Alloc, IntPtr.Zero);
		if (_state == IntPtr.Zero)
			throw new InvalidOperationException("Failed to create LuaState state");
	}

	public LuaState(IntPtr state)
	{
		_state = state;
		_ownsState = false;
	}

	public static LuaState FromIntPtr(IntPtr state)
	{
		LuaState? last = _lastFromIntPtr;
		if (last != null && last._state == state)
			return last;

		return _lastFromIntPtr = new(state);
	}

	public unsafe void InstallCallbacks(IEnumerable<string> atoms)
	{
		if (_atoms.Length == 0)
		{
			Dictionary<string, short> lookup = [];
			List<string> names = [];
			foreach (string atom in atoms)
			{
				if (names.Count < short.MaxValue && atom.Length <= 64 && lookup.TryAdd(atom, (short)names.Count))
					names.Add(atom);
			}
			_atomLookup = lookup.GetAlternateLookup<ReadOnlySpan<char>>();
			_atoms = [.. names];
		}

		((LuaCallbacks*)NativeBindings.lua_callbacks(_state))->useratom = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte*, nuint, short>)&UserAtom;

		_watchdog ??= new System.Threading.Timer(Watchdog, null, 1000, 1000);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static unsafe short UserAtom(IntPtr L, byte* s, nuint len)
	{
		if (len > 64)
			return -1;

		Span<char> chars = stackalloc char[(int)len];
		for (int i = 0; i < chars.Length; i++)
		{
			if (s[i] >= 0x80)
				return -1;
			chars[i] = (char)s[i];
		}

		return _atomLookup.TryGetValue(chars, out short atom) ? atom : (short)-1;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static unsafe void* Alloc(void* ud, void* ptr, nuint osize, nuint nsize)
	{
		if (nsize == 0)
		{
			NativeMemory.Free(ptr);
			_heapSize -= (long)osize;
			return null;
		}

		if (_resumeDepth > 0 && nsize > osize && _heapSize + (long)(nsize - osize) > HeapLimit)
			return null;

		void* block = NativeMemory.Realloc(ptr, nsize);
		if (block != null)
			_heapSize += (long)nsize - (long)osize;
		return block;
	}

	private static bool OverBudget()
	{
		return Stopwatch.GetTimestamp() - Volatile.Read(ref _resumeStart) >= ScriptTimeoutSec * Stopwatch.Frequency;
	}

	private static unsafe void Watchdog(object? _)
	{
		lock (_watchdogLock)
		{
			if (Volatile.Read(ref _resumeDepth) > 0 && _resumeCallbacks != IntPtr.Zero && OverBudget())
				((LuaCallbacks*)_resumeCallbacks)->interrupt = _interruptPtr;
		}
	}

	private static unsafe void Interrupt(IntPtr L, int gc)
	{
		if (gc >= 0)
			return;

		bool timedOut = _resumeDepth > 0 && OverBudget();
		if (!timedOut || _resumeDepth == 1)
			((LuaCallbacks*)NativeBindings.lua_callbacks(L))->interrupt = IntPtr.Zero;

		if (timedOut)
			NativeBindings.luaL_errorL(L, "script timed out after " + ScriptTimeoutSec + " seconds");
	}

	public static unsafe byte[] Compile(string sourceCode, IntPtr mutableGlobals = 0)
	{
		LuauCompileOptions options = new()
		{
			optimizationLevel = 2,
			vectorLib = _vectorLib,
			vectorCtor = _vectorCtor,
			vectorType = _vectorLib,
			mutableGlobals = mutableGlobals,
		};

		IntPtr size = new(Encoding.UTF8.GetByteCount(sourceCode));
		IntPtr bytecodePtr = NativeBindings.luau_compile(sourceCode, size, (IntPtr)(&options), out nint outSize);
		if (bytecodePtr == IntPtr.Zero)
		{
			throw new Exception("Error compiling Lua source code");
		}
		try
		{
			byte[] bytecode = GC.AllocateUninitializedArray<byte>(checked((int)outSize));
			Marshal.Copy(bytecodePtr, bytecode, 0, bytecode.Length);
			return bytecode;
		}
		finally
		{
			unsafe
			{
				NativeMemory.Free(bytecodePtr.ToPointer());
			}
		}
	}

	public LuaStatus Load(string name, byte[] compiled)
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

	public void SetSafeEnv(int index, int value)
	{
		NativeBindings.lua_setsafeenv(_state, index, value);
	}

	public LuaState(LuaState parent, IntPtr thread)
	{
		_state = thread;
		_parent = parent;
		_ownsState = false;
	}

	public void OpenLibs()
	{
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

	public static int UpValIndex(int index) => LUA_GLOBALSINDEX - index;

	/// <summary>
	/// Pushes an integer with value n onto the stack. 
	/// </summary>
	/// <param name="n"></param>
	public void PushInteger(long n)
	{
		NativeBindings.lua_pushinteger(_state, n);
	}

	public bool Next(int index)
	{
		return NativeBindings.lua_next(_state, index) != 0;
	}

	public int ObjLen(int idx)
	{
		return (int)NativeBindings.lua_objlen(_state, idx);
	}

	public int RawGet(int index)
	{
		return NativeBindings.lua_rawget(_state, index);
	}

	public void RawGetInteger(int index, int n)
	{
		NativeBindings.lua_rawgeti(_state, index, n);
	}

	public void RawSetInteger(int index, long i)
	{
		NativeBindings.lua_rawseti(_state, index, i);
	}

	public void RawSet(int index)
	{
		NativeBindings.lua_rawset(_state, index);
	}

	public void Insert(int index)
	{
		NativeBindings.lua_insert(_state, index);
	}

	public int Ref()
	{
		int refID = NativeBindings.lua_ref(_state, GetTop());
		Pop(1);
		return refID;
	}

	public void GetRef(int reference)
	{
		RawGetInteger(LUA_REGISTRYINDEX, reference);
	}

	public void Unref(int reference)
	{
		NativeBindings.lua_unref(_state, reference);
	}

	public int GarbageCollector(LuaGC what, int data)
	{
		return NativeBindings.lua_gc(_state, (int)what, data);
	}

	public int GetTop()
	{
		return NativeBindings.lua_gettop(_state);
	}

	public void SetTop(int index)
	{
		NativeBindings.lua_settop(_state, index);
	}

	public void Pop(int n)
	{
		NativeBindings.lua_settop(_state, -n - 1);
	}

	public void PushValue(int index)
	{
		NativeBindings.lua_pushvalue(_state, index);
	}

	public void SetTable(int index)
	{
		NativeBindings.lua_settable(_state, index);
	}

	public LuaType Type(int index)
	{
		return (LuaType)NativeBindings.lua_type(_state, index);
	}

	public string TypeName(int index)
	{
		int type = NativeBindings.lua_type(_state, index);
		IntPtr namePtr = NativeBindings.lua_typename(_state, type);
		return Marshal.PtrToStringAnsi(namePtr) ?? "unknown";
	}

	public bool IsString(int index) => Type(index) == LuaType.String;

	public bool IsNumber(int index) => Type(index) == LuaType.Number;

	public bool IsBoolean(int index) => Type(index) == LuaType.Boolean;

	public bool IsFunction(int index) => Type(index) == LuaType.Function;

	public bool IsNil(int index) => Type(index) == LuaType.Nil;

	public bool IsThread(int index) => Type(index) == LuaType.Thread;

	public bool IsBuffer(int index) => Type(index) == LuaType.Buffer;

	/// <summary>
	/// Converts the string or number at the index to a string.
	/// </summary>
	/// <returns>
	/// string on success and null on failure.
	/// </returns>
	/// <remarks>
	/// If the value on the stack is a number, it is coerced to a string value,
	/// changing the value at the index.
	/// </remarks>
	public string? ToString(int index)
	{
		IntPtr str = NativeBindings.lua_tolstring(_state, index, out nint len);
		return str != 0 ? Marshal.PtrToStringUTF8(str, (int)len) : null;
	}

	/// <summary>
	/// Converts the value at the index into a string that is placed on top of
	/// the stack (original is kept on the stack).
	/// </summary>
	/// <remarks>
	/// This conversion supports the __tostring metamethod of the value.
	/// </remarks>
	public string? LauxToString(int index)
	{
		IntPtr str = NativeBindings.luaL_tolstring(_state, index, out nint len);
		return str != 0 ? Marshal.PtrToStringUTF8(str, (int)len) : null;
	}

	public long ToInteger(int index)
	{
		return NativeBindings.lua_tointegerx(_state, index, out int _);
	}

	/// <summary>
	/// Converts the LuaState value at the given index to the signed integral type lua_Integer. The LuaState value must be an integer, or a number or string convertible to an integer (see §3.4.3); otherwise, lua_tointegerx returns 0. 
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	public long? ToIntegerX(int index)
	{
		long value = NativeBindings.lua_tointegerx(_state, index, out int isInteger);
		if (isInteger != 0)
			return value;
		return null;
	}

	/// <summary>
	/// Call metamethod of current metatable
	/// </summary>
	/// <param name="obj"></param>
	/// <param name="name"></param>
	/// <returns></returns>

	public bool CallMetamethod(int obj, string name)
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

	public double ToNumber(int index)
	{
		return NativeBindings.lua_tonumberx(_state, index, out _);
	}

	public bool ToBoolean(int index)
	{
		return NativeBindings.lua_toboolean(_state, index) != 0;
	}

	public LuaState ToThread(int index)
	{
		IntPtr state = NativeBindings.lua_tothread(_state, index);
		if (state == _state)
			return this;

		LuaState s = new(state);
		if (!s.IsAlive)
		{
			throw new LuaException("This state is dead");
		}
		return s;
	}

	public IntPtr ToUserData(int index)
	{
		return NativeBindings.lua_touserdata(_state, index);
	}

	public IntPtr ToPointer(int index)
	{
		return NativeBindings.lua_topointer(_state, index);
	}

	public bool IsLightUserData(int index) => Type(index) == LuaType.LightUserData;

	public bool IsUserData(int index) => Type(index) == LuaType.UserData;

	public bool IsTable(int index) => Type(index) == LuaType.Table;

	public void PushNil()
	{
		NativeBindings.lua_pushnil(_state);
	}

	public void PushNumber(double n)
	{
		NativeBindings.lua_pushnumber(_state, n);
	}

	public void PushString(string s)
	{
		NativeBindings.lua_pushstring(_state, s);
	}

	public void PushBoolean(bool b)
	{
		NativeBindings.lua_pushboolean(_state, b ? 1 : 0);
	}

	public void PushVector(Vector3 v)
	{
		NativeBindings.lua_pushvector(_state, v.X, v.Y, v.Z, 0);
	}

	public unsafe Vector3 ToVector(int index)
	{
		float* v = (float*)NativeBindings.lua_tovector(_state, index);
		return v == null ? Vector3.Zero : new Vector3(v[0], v[1], v[2]);
	}

	public void PushCFunction(LuaFunction func, string? name = null, int n = 0)
	{
		IntPtr userdataPtr = NewUserDataDTor((UIntPtr)IntPtr.Size, _functionDtor);

		GCHandle handle = GCHandle.Alloc(func);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);

		NativeBindings.lua_pushcclosurek(_state, func, name, n + 1, null);
	}

	private static void FunctionGarbageCollect(IntPtr ud)
	{
		IntPtr handlePtr = Marshal.ReadIntPtr(ud);
		if (handlePtr == IntPtr.Zero) return;
		GCHandle handle = GCHandle.FromIntPtr(handlePtr);

		if (handle.IsAllocated)
		{
			handle.Free();
		}
	}

	public bool PushThread()
	{
		return NativeBindings.lua_pushthread(_state) == 1;
	}

	public byte[] ToBuffer(int index)
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

	public void SetGlobal(string name)
	{
		SetField(LUA_GLOBALSINDEX, name);
	}

	public void GetGlobal(string name)
	{
		NativeBindings.lua_getfield(_state, LUA_GLOBALSINDEX, name);
	}

	public void GetTable(int index)
	{
		NativeBindings.lua_gettable(_state, index);
	}

	public LuaType GetMetaTable(int index)
	{
		return (LuaType)NativeBindings.lua_getmetatable(_state, index);
	}

	public void Register(string name, LuaFunction func)
	{
		PushCFunction(func, name);
		SetGlobal(name);
	}

	public void NewTable()
	{
		NativeBindings.lua_createtable(_state, 0, 0);
	}

	public void SetField(int index, string key)
	{
		NativeBindings.lua_setfield(_state, index, key);
	}

	public void GetField(int index, string key)
	{
		NativeBindings.lua_getfield(_state, index, key);
	}

	public void PushLightUserData(IntPtr ptr)
	{
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
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = NewUserDataDTor((UIntPtr)IntPtr.Size, _objectDtor);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);
	}

	private static void ManagedObjectGarbageCollect(IntPtr ud)
	{
		IntPtr handlePtr = Marshal.ReadIntPtr(ud);
		if (handlePtr == IntPtr.Zero) return;

		GCHandle handle = GCHandle.FromIntPtr(handlePtr);
		if (handle.IsAllocated)
			handle.Free();
	}

	public object? ToObject(int index)
	{
		if (IsNil(index) || !IsUserData(index))
			return null;

		IntPtr userdata = ToUserData(index);
		if (userdata == IntPtr.Zero)
			return null;
		IntPtr data = Marshal.ReadIntPtr(userdata);
		if (data == IntPtr.Zero)
			return null;

		var handle = GCHandle.FromIntPtr(data);
		if (!handle.IsAllocated)
			return null;

		return handle.Target;
	}

	public T? ToObject<T>(int index, bool freeGCHandle = true)
	{
		if (IsNil(index) || !IsUserData(index))
			return default;

		IntPtr userdata = ToUserData(index);
		if (userdata == IntPtr.Zero)
			return default;
		IntPtr data = Marshal.ReadIntPtr(userdata);
		if (data == IntPtr.Zero)
			return default;

		var handle = GCHandle.FromIntPtr(data);
		if (!handle.IsAllocated)
			return default;

		T? reference = (T?)handle.Target;

		if (freeGCHandle)
		{
			handle.Free();
			Marshal.WriteIntPtr(userdata, IntPtr.Zero);
		}

		return reference;
	}

	public void PushBuffer(byte[] buffer)
	{
		if (buffer == null)
		{
			PushNil();
			return;
		}

		IntPtr bufferPtr = NativeBindings.lua_newbuffer(_state, (UIntPtr)buffer.Length);

		if (bufferPtr == IntPtr.Zero)
		{
			throw new OutOfMemoryException("Lua failed to allocate the buffer.");
		}

		Marshal.Copy(buffer, 0, bufferPtr, buffer.Length);
	}

	public void SetMetaTable(int objIndex)
	{
		NativeBindings.lua_setmetatable(_state, objIndex);
	}

	public LuaState NewThread()
	{
		if (NativeBindings.lua_checkstack(_state, 1) == 0)
			throw new InvalidOperationException("Lua stack limit: unable to create new thread");

		IntPtr thread = NativeBindings.lua_newthread(_state);
		return new LuaState(this, thread);
	}

	public void Sandbox()
	{
		NativeBindings.luaL_sandbox(_state);
	}

	public void SandboxGlobals()
	{
		NewTable();

		NewTable();
		PushValue(LuaState.LUA_GLOBALSINDEX);
		SetField(-2, "__index");
		SetReadOnly(-1, true);
		SetMetaTable(-2);

		Replace(LuaState.LUA_GLOBALSINDEX);
		SetSafeEnv(LuaState.LUA_GLOBALSINDEX, 1);
	}

	public string? GetNameCallAtom(out int atom)
	{
		IntPtr ptr = NativeBindings.lua_namecallatom(_state, out atom);
		if (ptr == IntPtr.Zero)
		{
			atom = -1;
			return null;
		}
		if (atom >= 0 && atom < _atoms.Length) return _atoms[atom];
		atom = -1;
		return Marshal.PtrToStringUTF8(ptr);
	}

	public string? ToAtomString(int index, out int atom)
	{
		IntPtr ptr = NativeBindings.lua_tostringatom(_state, index, out atom);
		if (ptr != IntPtr.Zero && atom >= 0 && atom < _atoms.Length) return _atoms[atom];
		atom = -1;
		return ToString(index);
	}

	public void XMove(LuaState to, int n)
	{
		to.CheckStack(n);
		NativeBindings.lua_xmove(_state, to.State, n);
	}

	public void CheckStack(int n)
	{
		NativeBindings.lua_checkstack(_state, n);
	}

	public LuaStatus Resume(LuaState? from = null, int narg = 0, bool sameRun = false)
	{
		if (_resumeDepth == 0)
		{
			if (_heapSize > HeapLimit / 2 && Stopwatch.GetElapsedTime(_lastCollect).TotalSeconds >= 0.1)
			{
				_lastCollect = Stopwatch.GetTimestamp();
				GarbageCollector(LuaGC.Collect, 0);
			}
			_resumeCallbacks = NativeBindings.lua_callbacks(_state);
			if (!sameRun)
				_resumeStart = Stopwatch.GetTimestamp();
		}
		_resumeDepth++;

		try
		{
			return (LuaStatus)NativeBindings.lua_resume(_state, from?.State ?? IntPtr.Zero, narg);
		}
		finally
		{
			_resumeDepth--;
		}
	}

	public LuaStatus Status()
	{
		return (LuaStatus)NativeBindings.lua_status(_state);
	}

	public bool IsThreadReset()
	{
		return NativeBindings.lua_isthreadreset(_state) != 0;
	}

	public int Yield(int nresults)
	{
		return NativeBindings.lua_yield(_state, nresults);
	}

	public int Error(string value, params object[] v)
	{
		string message = v.Length > 0 ? string.Format(value, v) : value;
		return NativeBindings.luaL_errorL(_state, message);
	}

	public void PushLightUserdataTagged(IntPtr p, int tag = 0)
	{
		NativeBindings.lua_pushlightuserdatatagged(_state, p, tag);
	}

	public IntPtr NewUserDataTagged(UIntPtr size, int tag)
	{
		return NativeBindings.lua_newuserdatatagged(_state, size, tag);
	}

	public int UserDataTag(int index)
	{
		return NativeBindings.lua_userdatatag(_state, index);
	}

	public IntPtr ToUserDataTagged(int index, int tag)
	{
		return NativeBindings.lua_touserdatatagged(_state, index, tag);
	}

	public IntPtr NewUserDataDTor(UIntPtr size, LuaUserdataDestructor dtor)
	{
		if (!_pinnedDestructors.ContainsKey(dtor))
		{
			_pinnedDestructors[dtor] = GCHandle.Alloc(dtor);
		}
		return NativeBindings.lua_newuserdatadtor(_state, size, dtor);
	}

	public bool NewMetaTable(string name)
	{
		return NativeBindings.luaL_newmetatable(_state, name) != 0;
	}

	public string? DebugTrace()
	{
		IntPtr ptr = NativeBindings.lua_debugtrace(_state);
		return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : null;
	}

	/// <summary>
	/// Does the equivalent of t[p] = v, where t is the table at the given index, p is encoded as a light userdata, and v is the value at the top of the stack. 
	/// </summary>
	/// <param name="index"></param>
	/// <param name="obj"></param>
	public void RawSetPointer(int index, IntPtr obj)
	{
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
		return (LuaType)NativeBindings.lua_rawgetp(_state, index, obj);
	}

	public void SetThreadData(IntPtr data)
	{
		NativeBindings.lua_setthreaddata(_state, data);
	}

	public IntPtr GetThreadData()
	{
		return NativeBindings.lua_getthreaddata(_state);
	}

	public void Getfenv(int idx)
	{
		NativeBindings.lua_getfenv(_state, idx);
	}

	public int Setfenv(int idx)
	{
		return NativeBindings.lua_setfenv(_state, idx);
	}

	public void Replace(int idx)
	{
		NativeBindings.lua_replace(_state, idx);
	}

	public void Remove(int idx)
	{
		NativeBindings.lua_remove(_state, idx);
	}

	public void SetReadOnly(int idx, bool to)
	{
		NativeBindings.lua_setreadonly(_state, idx, to ? 1 : 0);
	}

	public void Call(int nargs, int nresults)
	{
		if (NativeBindings.lua_pcall(_state, nargs, nresults, 0) != (int)LuaStatus.OK)
		{
			string? error = ToString(-1);
			Pop(1);
			throw new LuaException($"Call error: {error}");
		}
	}

	public void Dispose()
	{
		if (!_disposed && _state != IntPtr.Zero)
		{
			if (_ownsState)
			{
				lock (_watchdogLock)
				{
					if (_resumeCallbacks == NativeBindings.lua_callbacks(_state))
						_resumeCallbacks = IntPtr.Zero;
				}
				NativeBindings.lua_close(_state);
			}
			_state = IntPtr.Zero;
			_disposed = true;
		}
		GC.SuppressFinalize(this);
	}

	public class LuaException(string message) : Exception(message)
	{
	}
}
