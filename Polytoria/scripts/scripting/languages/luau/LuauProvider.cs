// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using Polytoria.Datamodel.Services;
#if DEBUG
using Polytoria.DatamodelTest;
#endif
using Polytoria.Enums;
using Polytoria.Scripting.Extensions;
using Polytoria.Scripting.Libraries;
using Polytoria.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting.Luau;

public sealed partial class LuauProvider : IScriptLanguageProvider
{
	private const DynamicallyAccessedMemberTypes DynamicallyAccessedTypes = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods;
	private const int GCStepThreshold = 100;

	private static readonly Dictionary<Type, MethodInfo?> _gdToProxy = [];
	private static readonly Dictionary<IntPtr, PTCallbackData> _ptrToCallback = [];
	private static readonly Dictionary<PTCallbackData, IntPtr> _callbackToPtr = [];
	private static readonly Dictionary<IntPtr, object> _ptrToObject = [];
	private const string WeakUserdataCache = "__UDCACHE";
	private static readonly int ThreadDataKey = 0x1247;

	private static int _allocsSinceLastGC = 0;

	internal LuaState GlobalLuaState = null!;

	private static readonly IntPtr _internalScriptPtr = 0x61;
	private static readonly IntPtr _loggerPtr = 0x67;

	public static readonly Dictionary<string, Type> LuaLibraries = new()
	{
		{ "json", typeof(LuaLibJSON) },
		{ "guid", typeof(LuaLibGUID) },
	};

	public static readonly Dictionary<string, Type> LuaExtensions = new()
	{
		{ "math", typeof(LuaExtensionMath) },
	};

	public static readonly Dictionary<string, Type> EnumMapCompatibility = new()
	{
		{ "CameraMode", typeof(Camera.LegacyCameraModeEnum) },
		{ "CollisionType", typeof(Datamodel.Mesh.CollisionTypeEnum) },
		{ "TextFontPreset", typeof(BuiltInFontAsset.BuiltInTextFontPresetEnum) },
		{ "TextJustify", typeof(TextVerticalAlignmentEnum) },
		{ "TweenType", typeof(LeanTweenType) },
		{ "KeyCode", typeof(LegacyKeyCode) },
		{ "PartShape", typeof(Part.LegacyShapeEnum) },
	};

	public static readonly string[] DisallowedGlobals =
	[
		"load",
		"loadfile",
		"loadstring",
		"dofile",
		"package",
		"io",
		"setfenv",
		"getfenv",
		"jit",
		"ffi",
		"module",
		"thread",
		"newproxy",
	];

	public static LuauProvider Singleton { get; private set; } = null!;

	static LuauProvider()
	{
		foreach ((Type target, Type proxy) in ScriptService.ProxyMap)
		{
			RegisterProxy(target, proxy);
		}
	}

	public LuauProvider()
	{
		Singleton = this;
		LuaState state = new();
		GlobalLuaState = state;
		InitializeCache(state);
		state.OpenLibs();

		foreach (string item in DisallowedGlobals)
		{
			state.PushNil();
			state.SetGlobal(item);
		}

		// Register custom coroutine.resume
		state.GetGlobal("coroutine");
		state.PushCFunction(LuaCoroutineResume, "coroutine.resume");
		state.SetField(-2, "resume");

		state.PushCFunction(LuaCoroutineWrap, "coroutine.wrap");
		state.SetField(-2, "wrap");

		RegisterLuaExtensions(state);

		// Set all global library tables to read-only
		state.PushNil();
		while (state.Next(LuaState.LUA_GLOBALSINDEX))
		{
			if (state.IsTable(-1))
				state.SetReadOnly(-1, true);
			state.Pop(1);
		}

		// Set all builtin metatables to read-only
		state.PushString("");
		if (state.GetMetaTable(-1) != LuaType.Nil)
		{
			state.SetReadOnly(-1, true);
			state.Pop(2); // pop metatable + string
		}
		else
		{
			state.Pop(1); // pop string
		}
	}

	public void Run(Script script)
	{
		PT.Print("Running script: ", script.LuaPath);
		LuaState state = InitalizeScript(script);

		// Try compile
		try
		{
			script.TryCompile();
		}
		catch (Exception e)
		{
			script.Root.ScriptService.Logger.LogError(script, e.Message);
			return;
		}

		// Load & Run
		try
		{
			state.Load(script.LuaPath, script.Bytecode!);

			async void run()
			{
				await ResumeThread(state, null, 0, isMainThread: true);
			}

			run();
		}
		catch (Exception e)
		{
			script.Root.ScriptService.Logger.LogError(script, e.Message);
		}
	}

	public byte[] CompileSource(string source)
	{
		return LuaState.Compile(source);
	}

	public LuaState InitalizeScript(Script script)
	{
		LuaState state = NewThread(GlobalLuaState);
		script.LuauState = state;

		state.SandboxGlobals();

		// Internal script
		SetGlobalTablePtr(state, _internalScriptPtr, script);
		SetGlobalTablePtr(state, _loggerPtr, script.Root.ScriptService.Logger);

		state.Register("print", LuaPrint);
		state.Register("wait", LuaWait);
		state.Register("spawn", LuaSpawn);
		state.Register("tick", LuaTick);
		state.Register("time", LuaTime);
		state.Register("require", LuaRequire);
		state.Register("pcall", LuaPCall);

#if DEBUG
		// Test special function
		if (DatamodelTestEntry.IsTesting)
		{
			state.Register("quit", LuaDMTestQuit);
		}
#endif

		// Randomize on start
		GD.Randomize();
		state.GetGlobal("math");
		state.GetField(-1, "randomseed");
		state.PushInteger((long)(GD.Randf() * 100000));
		state.Call(1, 0);
		state.Pop(1);

		foreach ((string name, Type lib) in LuaLibraries)
		{
			PushCSClass(state, lib);
			state.SetGlobal(name);
		}

		foreach ((string name, Type lib) in ScriptService.GlobalDataMap)
		{
			PushCSClass(state, lib);
			state.SetGlobal(name);
		}

		state.PushString(Globals.AppVersion);
		state.SetGlobal("_POLY_VERSION");

		state.PushBoolean(true);
		state.SetGlobal("_POLY_2");

		Assembly assembly = Assembly.GetExecutingAssembly();
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
		Type[] types = assembly.GetTypes();
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code

		// Expose all instantiatables
		foreach (var t in types)
		{
			if (!t.IsDefined(typeof(InstantiableAttribute), false)) continue;
#pragma warning disable IL2072 // Datamodel types has the reflections
			PushCSClass(state, t);
#pragma warning restore IL2072 // Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.
			state.SetGlobal(t.Name);
		}

		Dictionary<string, IScriptObject?> staticObjects = ScriptService.GetStaticObjects(script.Root, script);

		foreach ((string key, IScriptObject? instance) in staticObjects)
		{
			PushValueToLua(state, instance);
			state.SetGlobal(key);
		}

		PushValueToLua(state, script);
		state.SetGlobal("script");

		PushValueToLua(state, script.Root);
		state.SetGlobal("game");

		PushValueToLua(state, script.Root);
		state.SetGlobal("world");

		PushCSClass(state, typeof(Instance));
		state.SetGlobal("Instance");

		if (!script.Compatibility)
		{
			state.NewTable();
		}

		foreach ((string name, Type e) in ScriptService.EnumMap)
		{
			PushEnumTable(state, e);
			if (script.Compatibility)
			{
				state.SetGlobal(name);
			}
			else
			{
				state.SetField(-2, name);
			}
		}

		if (!script.Compatibility)
		{
			state.SetGlobal("Enums");
		}

		// Extra enum compatibility map
		if (script.Compatibility)
		{
			foreach ((string name, Type e) in EnumMapCompatibility)
			{
				PushEnumTable(state, e);
				state.SetGlobal(name);
			}
		}

		state.PushBoolean(script.Root.WorldID == 0);
		state.SetGlobal("_LOCALTEST");

		var mainThread = NewThread(state);
		script.LuauMainThread = mainThread;

		return mainThread;
	}

	public async Task CallAsync(Script script, string funcName, object?[]? args)
	{
		if (script.LuauMainThread == null || !script.LuauMainThread.IsAlive)
		{
			throw new Exception("Target script is not active");
		}

		LuaState mainThread = script.LuauMainThread;
		LuaState co;
		int coRef;

		lock (mainThread)
		{
			mainThread.GetGlobal(funcName);
			if (!mainThread.IsFunction(-1))
			{
				mainThread.Pop(1);
				return;
			}

			co = NewThread(mainThread);
			coRef = mainThread.Ref();

			mainThread.XMove(co, 1);

			if (args != null)
			{
				foreach (object? arg in args)
					PushValueToLua(co, arg);
			}
		}

		try
		{
			await ResumeThread(co, mainThread, args?.Length ?? 0);
		}
		finally
		{
			lock (mainThread)
				mainThread.Unref(coRef);
		}
	}

	public void Close(Script script)
	{
		if (script.LuauState == null) return;

		// Free function pointer references
		foreach (IntPtr funcPtr in script.LuauFunctionPointers)
		{
			if (_ptrToCallback.TryGetValue(funcPtr, out PTCallbackData func))
			{
				func.Callback.Dispose();
				_callbackToPtr.Remove(func);
			}
			_ptrToCallback.Remove(funcPtr);
		}

		PTSignal.CleanupScript(script);
	}

	public void CallUpdate(Script script, double delta)
	{
		if (script.LuauMainThread == null || !script.LuauMainThread.IsAlive) return;
		string updateKeyword = "_Update";

		if (script.Compatibility)
		{
			updateKeyword = "_update";
		}

		CallAsync(script, updateKeyword, [delta]).Wait();
	}

	public void CallFixedUpdate(Script script, double delta)
	{
		if (script.LuauMainThread == null || !script.LuauMainThread.IsAlive) return;
		string updateKeyword = "_FixedUpdate";

		if (script.Compatibility)
		{
			updateKeyword = "_fixed_update";
		}

		CallAsync(script, updateKeyword, [delta]).Wait();
	}

	private void RegisterLuaExtensions(LuaState state)
	{
		Script s = GetScriptInstance(state);
		foreach ((string libName, Type type) in LuaExtensions)
		{
			state.GetGlobal(libName);
			try
			{
				MethodInfo[] methods = type.GetMethods();

				foreach (MethodInfo method in methods)
				{
					ScriptMethodAttribute? attr = method.GetCustomAttribute<ScriptMethodAttribute>();
					if (attr == null)
						continue;
					HandlesLuaStateAttribute? handleLua = method.GetCustomAttribute<HandlesLuaStateAttribute>();

					string methodName = attr.MethodName ?? method.Name;

					int luaExt(IntPtr L)
					{
						LuaState innerState = LuaState.FromIntPtr(L);

						if (handleLua != null)
						{
							return (int)method.Invoke(null, [innerState])!;
						}

						int top = innerState.GetTop();

						List<object?> argList = [];
						for (int i = 0; i < top; i++)
						{
							object? arg = LuaToObject(innerState, i + 1, false);
							argList.Add(arg);
						}
						object?[] args = [.. argList];

						try
						{
							object? val = method.Invoke(null, args);
							PushValueToLua(innerState, val);
							return 1;
						}
						catch (Exception ex)
						{
							innerState.Error(ex.Message);
							return 0;
						}
					}

					state.PushCFunction(luaExt, libName + "." + methodName);
					state.SetField(-2, methodName);
				}
			}
			finally
			{
				state.Pop(1);
			}
		}
	}

	public static LuaState NewThread(LuaState from)
	{
		LuaState co = from.NewThread();
		return co;
	}

	public static async Task ResumeThread(LuaState thread, LuaState? from, int narg = 0, bool throwError = false, bool isMainThread = false)
	{
		Script script;
		LogDispatcher logger;

		int threadRef;

		lock (thread)
		{
			script = GetScriptInstance(thread);
			logger = GetLogger(thread);
			thread.PushThread();
			threadRef = thread.Ref();
		}

		try
		{
			if (script == null)
			{
				PT.PrintErr("Script not present in registry");
				return;
			}

			while (true)
			{
				if (thread == null)
				{
					PT.PrintErr("Thread's null");
					break;
				}

				if (!thread.IsAlive)
				{
					PT.PrintErr("Pointer's null");
					break;
				}

				if (!script.ShouldContinue) return;

				LuaStatus status;
				lock (thread)
				{
					status = thread.Resume(from, narg);
				}

				if (!script.ShouldContinue) return;

				if (status == LuaStatus.OK || status == LuaStatus.Break)
				{
					break;
				}
				else if (status == LuaStatus.Yield)
				{
					ThreadData? threadData;
					lock (thread)
					{
						threadData = GetThreadData(thread);
					}
					if (threadData.HasValue)
					{
						// called via built-in function, wait
						Task<int> tsk = threadData.Value.Task;
						narg = await tsk;
						continue;
					}
					else
					{
						// called via coroutine.yield(), break here
						break;
					}
				}
				else
				{
					throw new Exception(thread.ToString(-1));
				}
			}
		}
		catch (Exception e)
		{
			e = e.InnerException ?? e;

			string errorMsg = e.Message;

			if (e is SEHException)
			{
				errorMsg = thread.ToString(-1) ?? e.Message;
			}

			if (isMainThread)
			{
				script.LuauMainThread = null;
			}

			if (throwError)
			{
				throw new LuaState.LuaException(errorMsg);
			}
			else
			{
				string errContent = $"[{script.LuaPath}] {e.Message}";
				string? traceback;
				lock (thread)
				{
					traceback = thread.DebugTrace();
				}

				if (traceback != null)
				{
					errContent = errContent + "\nstacktrace:\n" + traceback;
				}

				logger.LogError(script, errContent);
			}
		}
		finally
		{
			lock (thread)
			{
				thread.Unref(threadRef);
			}
		}
	}

	public static void RegisterProxy(Type target, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type proxy)
	{
		_gdToProxy[target] = proxy.GetMethod(nameof(IScriptGDObject.FromGDClass), BindingFlags.Public | BindingFlags.Static);
	}

	public static int LuaPrint(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);
		Script script = GetScriptInstance(lua);
		LogDispatcher logger = GetLogger(lua);

		int n = lua.GetTop();
		StringBuilder sb = new();

		for (int i = 1; i <= n; i++)
		{
			if (i > 1)
				sb.Append('\t');
			LuaType dataType = lua.Type(i);
			if (dataType == LuaType.Boolean)
			{
				sb.Append(lua.ToBoolean(i));
			}
			else if (dataType == LuaType.Number)
			{
				sb.Append(lua.ToNumber(i));
			}
			else
			{
				sb.Append(lua.ToString(i, true) ?? "<" + lua.TypeName(i) + ">");
			}
		}
		string logInfo = sb.ToString();
		logger.LogInfo(script, logInfo);
		return 0;
	}
	public int LuaWait(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);

		double n;

		if (lua.IsNumber(1))
		{
			n = lua.ToNumber(1);
		}
		else
		{
			n = 0;
		}

		TaskCompletionSource<int> tcs = new();

		SetYieldTask(lua, tcs.Task);

		async void RunAsync()
		{
			if (n != 0)
			{
				await Globals.Singleton.WaitAsync((float)n);
			}
			else
			{
				await Globals.Singleton.WaitPhysicsFrame();
			}

			PushValueToLua(lua, true);
			tcs.SetResult(1);
		}

		RunAsync();

		return lua.Yield(1);
	}

	public int LuaTime(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);
		Script script = GetScriptInstance(lua);

		PushValueToLua(lua, script.Root.UpTime);

		return 1;
	}

	public int LuaRequire(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);

		Script script = GetScriptInstance(state);
		object? obj = LuaToObject(state, 1);

		ModuleScript? ms = null;
		string? source = null;
		string chunkName = "";

		if (obj != null && obj is ModuleScript moduleScript)
		{
			ms = moduleScript;
			source = ms.Source;
			chunkName = moduleScript.LuaPath;
		}

		if (ms == null || source == null)
		{
			return 0;
		}

		// Return existing if already required
		if (ms.LuauState != null)
		{
			if (ms.CachedLuauResultRef.HasValue)
			{
				state.GetRef(ms.CachedLuauResultRef.Value);
				return 1;
			}
			return 0;
		}

		LuaState co = InitalizeScript(ms);
		int coRef = state.Ref();

		// Sandbox thread
		co.SandboxGlobals();

		// Global to identify if calling from server/client
		bool isClient = script is ClientScript;
		co.PushBoolean(isClient);
		co.SetGlobal("_CLIENT");
		co.PushBoolean(!isClient);
		co.SetGlobal("_SERVER");

		Exception? capturedException1 = null;

		// Try compile
		try
		{
			ms.TryCompile();
		}
		catch (Exception e)
		{
			capturedException1 = e;
		}

		if (capturedException1 != null)
		{
			state.Unref(coRef);
			return state.Error(co.ToString(-1) ?? capturedException1.Message);
		}

		Exception? capturedException2 = null;

		// Load source
		try
		{
			co.Load(chunkName, ms.Bytecode!);
		}
		catch (Exception ex)
		{
			capturedException2 = ex;
		}

		// Caught error
		if (capturedException2 != null)
		{
			state.Unref(coRef);
			return state.Error(co.ToString(-1) ?? capturedException2.Message);
		}

		TaskCompletionSource<int> tcs = new();
		SetYieldTask(state, tcs.Task);

		_ = HandleRequireAsync(co, state, coRef, chunkName, tcs, ms);

		return state.Yield(1);
	}

	private static async Task HandleRequireAsync(LuaState co, LuaState state, int coRef, string chunkName, TaskCompletionSource<int> tcs, ModuleScript ms)
	{
		try
		{
			await ResumeThread(co, state, 0, true);

			int top = co.GetTop();
			if (top > 0)
			{
				for (int i = 1; i <= top; i++)
					co.PushValue(i);
				co.XMove(state, top);

				state.PushValue(-top); // push copy of first result
				ms.CachedLuauResultRef = state.Ref();
			}
			tcs.SetResult(top);
		}
		catch (Exception ex)
		{
			tcs.SetException(new Exception($"Failure when requiring {chunkName}: {ex.Message}"));
		}
		finally
		{
			state.Unref(coRef);
		}
	}

	public static int LuaSpawn(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);

		if (!state.IsFunction(1))
		{
			state.Error("spawn requires a function");
			return 0;
		}

		int argCount = state.GetTop();
		int numArgs = argCount - 1;

		state.PushValue(1);
		int funcRef = state.Ref();

		LuaState co = NewThread(state);
		int coRef = state.Ref();

		state.GetRef(funcRef);
		state.XMove(co, 1);

		// Move arguments to the new thread
		if (numArgs > 0)
		{
			for (int i = 2; i <= argCount; i++)
				state.PushValue(i);
			state.XMove(co, numArgs);
		}

		state.Unref(funcRef);

		async void run()
		{
			try
			{
				await ResumeThread(co, null, numArgs);
			}
			finally
			{
				co.Unref(coRef);
			}
		}

		run();

		return 0;
	}


	public int LuaPCall(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		if (!state.IsFunction(1))
		{
			state.Error("pcall requires a function");
			return 0;
		}

		int nargs = state.GetTop() - 1;

		state.PushValue(1);
		int funcRef = state.Ref();
		LuaState co = NewThread(state);

		int coRef = state.Ref();

		state.GetRef(funcRef);
		state.XMove(co, 1);

		// Move arguments onto the coroutine stack
		if (nargs > 0)
		{
			for (int i = 2; i <= nargs + 1; i++)
			{
				state.PushValue(i);
			}
			state.XMove(co, nargs);
		}

		TaskCompletionSource<int> tcs = new();
		SetYieldTask(state, tcs.Task);

		_ = HandlePCallAsync(co, state, funcRef, coRef, nargs, tcs);

		return state.Yield(2);
	}

	private async Task HandlePCallAsync(LuaState co, LuaState state, int funcRef, int coRef, int nargs, TaskCompletionSource<int> tcs)
	{
		try
		{
			await ResumeThread(co, state, nargs, true);
			int nresults = co.GetTop();
			PushValueToLua(state, true);
			if (nresults > 0)
			{
				co.XMove(state, nresults);
			}
			tcs.SetResult(1 + nresults);
		}
		catch (Exception ex)
		{
			PushValueToLua(state, false);
			PushValueToLua(state, ex.InnerException?.Message ?? ex.Message);
			tcs.SetResult(2);
		}
		finally
		{
			state.Unref(funcRef);
			state.Unref(coRef);
		}
	}

	public static int LuaTick(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		state.PushNumber((double)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
		return 1;
	}

	public static int LuaCoroutineResume(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);

		if (!lua.IsThread(1))
		{
			return lua.Error("coroutine.resume requires a thread");
		}

		async void run()
		{
			await ResumeThread(lua.ToThread(1), lua, lua.GetTop() - 1);
		}

		run();

		return 0;
	}

	public static int LuaCoroutineWrap(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);

		if (!lua.IsFunction(1))
		{
			return lua.Error("coroutine.wrap requires a function");
		}

		LuaState newThread = NewThread(lua);
		lua.PushValue(1);
		lua.XMove(newThread, 1);
		int threadRef = lua.Ref();

		LuaWrappedCoroutine c = new() { ThreadRef = threadRef };

		GCHandle handle = GCHandle.Alloc(c);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = lua.NewUserDataDTor((UIntPtr)IntPtr.Size, GarbageCollect);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);

		lua.NewTable();

		lua.PushCFunction(c.WrapCall, "__call");
		lua.SetField(-2, "__call");

		lua.PushBoolean(false);
		lua.SetField(-2, "__metatable");

		lua.SetMetaTable(-2);

		return 1;
	}

#if DEBUG
	public static int LuaDMTestQuit(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);

		if (!lua.IsNumber(1))
		{
			return lua.Error("quit requires an exit code");
		}

		Globals.Singleton.Quit(force: true, code: (int)lua.ToNumber(1));

		return 0;
	}

#endif

	public static void SetYieldTask(LuaState state, Task<int> tsk)
	{
		ThreadData thread = GetThreadData(state, true) ?? new();
		thread.Task = tsk;
		SetThreadData(state, thread);
	}

	public static void SetYieldTaskSource(LuaState state, TaskCompletionSource<int> tsk)
	{
		ThreadData thread = GetThreadData(state, true) ?? new();
		thread.TaskSource = tsk;
		thread.Task = tsk.Task;
		SetThreadData(state, thread);
	}

	public static TaskCompletionSource<int>? GetYieldTaskSource(LuaState state)
	{
		ThreadData? thread = GetThreadData(state, false);
		if (thread == null) return null;
		return thread.Value.TaskSource;
	}

	private static void SetThreadData(LuaState state, ThreadData threadData)
	{
		GCHandle handle = GCHandle.Alloc(threadData);
		IntPtr ptr = GCHandle.ToIntPtr(handle);
		state.SetThreadData(ptr);
	}

	private static ThreadData? GetThreadData(LuaState state, bool freeHandle = true)
	{
		IntPtr ptr = state.GetThreadData();
		if (ptr == IntPtr.Zero)
			return null;

		GCHandle handle = GCHandle.FromIntPtr(ptr);
		ThreadData task = (ThreadData)handle.Target!;
		if (freeHandle)
		{
			handle.Free();
		}

		state.SetThreadData(IntPtr.Zero);

		return task;
	}

	public void PushValueToLua(LuaState state, object? value)
	{
		Type? valType = value?.GetType();

		// TODO: Refactor this so it's not one gazillion if-elses
		if (value == null)
		{
			state.PushNil();
		}
		else if (value is string strVal)
		{
			state.PushString(strVal);
		}
		else if (value is int intVal)
		{
			state.PushNumber(intVal);
		}
		else if (value is uint uintVal)
		{
			state.PushNumber(uintVal);
		}
		else if (value is ulong ulongVal)
		{
			state.PushNumber(ulongVal);
		}
		else if (value is long longVal)
		{
			state.PushNumber(longVal);
		}
		else if (value is double dblVal)
		{
			state.PushNumber(dblVal);
		}
		else if (value is decimal decimalVal)
		{
			state.PushNumber((double)decimalVal);
		}
		else if (value is float flVal)
		{
			state.PushNumber(flVal);
		}
		else if (value is bool boolVal)
		{
			state.PushBoolean(boolVal);
		}
		else if (value is byte[] byteArrayVal)
		{
			state.PushBuffer(byteArrayVal);
		}
		else if (valType != null && valType.IsEnum) // Handle enums
		{
			Type underlyingType = Enum.GetUnderlyingType(valType);
			object numericValue = Convert.ChangeType(value, underlyingType);
			int enumVal = Convert.ToInt32(numericValue);

			PushEnum(state, valType, enumVal);
		}
		else if (value is IDictionary<string, object> dict) // Handle string key Dictionaries
		{
			state.NewTable();
			foreach ((string key, object val) in dict)
			{
				state.PushString(key);
				PushValueToLua(state, val);
				state.SetTable(-3);
			}
		}
		else if (value is IDictionary<object, object> odict) // Handle Dictionaries
		{
			state.NewTable();
			foreach ((object key, object val) in odict)
			{
				PushValueToLua(state, key);
				PushValueToLua(state, val);
				state.SetTable(-3);
			}
		}
		else if (value is IScriptObject sc) // Handle script objects
		{
			PushCSClass(state, sc);
		}
		else if (_gdToProxy.TryGetValue(value.GetType(), out MethodInfo? fromGDClass)) // Handle proxies
		{
			IScriptObject? proxy = fromGDClass?.Invoke(null, [value]) as IScriptObject
								   ?? value as IScriptObject;

			if (proxy != null)
				PushCSClass(state, proxy);
			else
				state.PushNil();
		}
		else if (value is IEnumerable enumerable) // Handle arrays/lists
		{
			state.NewTable();
			int tableIndex = state.GetTop();

			int index = 1;
			foreach (object? item in enumerable)
			{
				state.PushInteger(index);
				PushValueToLua(state, item);
				state.SetTable(tableIndex);
				index++;
			}
		}
		else // Fallback for unsupported types
		{
			GD.PushError("PushValueToLua Unsupported type: ", value.GetType());
			state.PushNil();
		}
	}

	public static void DebugShowStack(LuaState lua)
	{
		int top = lua.GetTop();
		PT.Print($"Debug stack {top} ---");

		for (int i = -2; i <= top; i++)
		{
			LuaType t = lua.Type(i);
			PT.Print($"[{i}] {lua.Type(i)} {lua.TypeName(i)}");
			if (t == LuaType.String)
			{
				PT.Print("CONTENT: ", lua.ToString(i));
			}
			else if (t == LuaType.UserData)
			{
				PT.Print("CONTENT: ", lua.ToUserData(i));
			}
		}
	}

	public object? LuaToObject(LuaState state, int index, bool convertToGD = true, bool getAsFunction = false)
	{
		return LuaToObjectInternal(state, index, convertToGD, getAsFunction, []);
	}

	internal object? LuaToObjectInternal(LuaState state, int index, bool convertToGD = true, bool getAsFunction = false, HashSet<IntPtr> visitedTables = null!)
	{
		if (state.IsNumber(index)) return state.ToNumber(index); // number
		if (state.IsString(index)) return state.ToString(index); // string
		if (state.IsBoolean(index)) return state.ToBoolean(index); // boolean
		if (state.IsUserData(index)) // userdata
		{
			IntPtr ptr = state.ToUserData(index);
			if (ptr == IntPtr.Zero)
			{
				GD.PushError("Pointer null");
				return null;
			}
			IntPtr handlePtr = Marshal.ReadIntPtr(ptr);

			// Convert back to a GCHandle
			GCHandle handle = GCHandle.FromIntPtr(handlePtr);

			// Get original managed object
			object? obj = handle.Target;

			if (obj is IScriptGDObject gdData && convertToGD)
			{
				return gdData.ToGDClass();
			}

			if (obj is IScriptObject data)
			{
				return data;
			}

			// Handle enum
			if (obj is int i)
			{
				return i;
			}
		}
		else if (state.IsFunction(index) && getAsFunction) // PTFunction
		{
			Script script = GetScriptInstance(state);

			state.PushValue(index);
			int funcRef = state.Ref();

			LuaState mainState = script.LuauState ?? throw new Exception("INTERNAL BUG: No main thread");

			PTFunction del = new(async (args) =>
			{
				if (!mainState.IsAlive) return [];

				LuaState co;
				int coRef;

				lock (mainState)
				{
					co = NewThread(mainState);
					coRef = mainState.Ref();

					mainState.GetRef(funcRef);
					mainState.XMove(co, 1);

					foreach (object? arg in args)
					{
						PushValueToLua(co, arg);
					}
				}

				await ResumeThread(co, state, args.Length, true);

				try
				{
					int top = co.GetTop();
					List<object?> returnArgs = [];

					if (top > 0)
					{
						for (int i = 1; i <= top; i++)
						{
							returnArgs.Add(LuaToObject(co, i));
						}
					}
					return [.. returnArgs];
				}
				catch
				{
					throw;
				}
				finally
				{
					mainState.Unref(coRef);
				}
			})
			{
				LangProvider = this
			};

			return del;
		}
		else if (state.IsFunction(index) && !getAsFunction) // PTCallback
		{
			IntPtr funcPtr = state.ToPointer(index);
			if (_ptrToCallback.TryGetValue(funcPtr, out PTCallbackData cached))
			{
				return cached.Callback;
			}

			Script script = GetScriptInstance(state);

			state.PushValue(index);
			int funcRef = state.Ref();

			LuaState mainState = script.LuauState ?? throw new Exception("INTERNAL BUG: No main thread");

			LuaState handler = NewThread(mainState);
			int handlerRef = mainState.Ref();

			PTCallback del = new(async (args) =>
			{
				if (!mainState.IsAlive) return;

				LuaState co;
				int coRef;

				co = NewThread(handler);
				coRef = handler.Ref();

				handler.GetRef(funcRef);
				handler.XMove(co, 1);

				foreach (object? arg in args)
				{
					PushValueToLua(co, arg);
				}

				try
				{
					await ResumeThread(co, handler, args.Length);
				}
				finally
				{
					handler.Unref(coRef);
				}
			})
			{
				LangProvider = this,
				FromScript = script
			}
		;

			PTCallbackData data = new()
			{
				RefID = funcRef,
				HandlerRefID = handlerRef,
				FuncPtr = funcPtr,
				Callback = del,
				State = mainState
			};

			_ptrToCallback[funcPtr] = data;
			_callbackToPtr[data] = funcPtr;
			script.LuauFunctionPointers.Add(funcPtr);

			return del;
		}
		else if (state.IsTable(index)) // Tables
		{
			IntPtr tablePtr = state.ToPointer(index);

			if (!visitedTables.Add(tablePtr))
			{
				// Circular reference, return null
				return null;
			}

			try
			{
				int absindex = state.AbsIndex(index);
				int startTop = state.GetTop();

				Dictionary<int, object?> intKeyed = [];
				Dictionary<object, object?> otherKeyed = [];
				int maxIndex = 0;
				state.PushNil(); // first key
				try
				{
					while (state.Next(absindex))
					{
						try
						{
							if (state.IsNumber(-2))
							{
								double n = state.ToNumber(-2);
								int intKey = (int)n;
								if (intKey >= 1 && Math.Abs(n - intKey) < 1e-10)
								{
									intKeyed[intKey] = LuaToObjectInternal(state, -1, convertToGD, false, visitedTables);
									if (intKey > maxIndex) maxIndex = intKey;
								}
								else
								{
									otherKeyed[LuaToObjectInternal(state, -2, convertToGD, false, visitedTables) ?? ""]
										= LuaToObjectInternal(state, -1, convertToGD, false, visitedTables);
								}
							}
							else
							{
								otherKeyed[LuaToObjectInternal(state, -2, convertToGD, false, visitedTables) ?? ""]
									= LuaToObjectInternal(state, -1, convertToGD, false, visitedTables);
							}
						}
						finally
						{
							state.Pop(1);
						}
					}
				}
				catch
				{
					// Clean up stack on error
					state.SetTop(startTop);
					throw;
				}

				// Finalize if it's array or dictionary
				if (otherKeyed.Count == 0 && intKeyed.Count > 0)
				{
					object?[] array = new object?[maxIndex];
					foreach ((int key, object? val) in intKeyed)
					{
						array[key - 1] = val;
					}
					return array;
				}
				else
				{
					if (intKeyed.Count == 0)
						return otherKeyed;
					if (otherKeyed.Count == 0)
					{
						Dictionary<object, object?> result = new(intKeyed.Count);
						foreach ((int key, object? val) in intKeyed)
						{
							result[key] = val;
						}
						return result;
					}
					Dictionary<object, object?> dict = new(intKeyed.Count + otherKeyed.Count);
					foreach ((int key, object? val) in intKeyed)
					{
						dict[key] = val;
					}
					foreach ((object key, object? val) in otherKeyed)
					{
						dict[key] = val;
					}
					return dict;
				}
			}
			finally
			{
				// Allow this table to be visited again via a different path
				visitedTables.Remove(tablePtr);
			}
		}
		else if (state.IsBuffer(index))
		{
			return state.ToBuffer(index);
		}
		return null;
	}

	public void PushCSClass(LuaState lua, IScriptObject obj)
	{
		PushCSClassInternal(lua, obj, null);
	}

	public void PushCSClass(LuaState lua, [DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type type)
	{
		PushCSClassInternal(lua, null, type);
	}

	public void PushEnumTable(LuaState lua, Type specifyType)
	{
		lua.NewTable();
		foreach (string item in Enum.GetNames(specifyType))
		{
			object value = Enum.Parse(specifyType, item);
			int v = Convert.ToInt32(value);

			PushEnum(lua, specifyType, v);
			lua.SetField(-2, item);
		}
	}

	private static void GarbageCollect(IntPtr ud)
	{
		IntPtr handlePtr = Marshal.ReadIntPtr(ud);
		if (handlePtr == IntPtr.Zero) return;

		_ptrToObject.Remove(handlePtr);

		GCHandle handle = GCHandle.FromIntPtr(handlePtr);
		if (handle.IsAllocated)
			handle.Free();
	}

	public void PushEnum(LuaState lua, Type specifyType, object value)
	{
		Script script = GetScriptInstance(lua);

		GCHandle handle = GCHandle.Alloc(value);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = lua.NewUserDataDTor((UIntPtr)IntPtr.Size, GarbageCollect);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);

		_ptrToObject.Add(handlePtr, value);

		lua.GetField(LuaState.LUA_REGISTRYINDEX, specifyType.Name);
		if (lua.Type(-1) == LuaType.Nil)
		{
			lua.Pop(1);
			lua.NewMetaTable(specifyType.Name);

			LuaEnum enumMeta = new()
			{
				Lua = lua,
				TargetType = specifyType,
				LangProvider = this,
			};

			enumMeta.RegisterMetamethods();
		}

		lua.PushBoolean(false);
		lua.SetField(-2, "__metatable");

		lua.SetMetaTable(-2);
	}

	private static string GetRegKeyFromObj(object obj)
	{
		return "__userdata_" + obj.GetType().Name + obj.GetHashCode();
	}

	private void PushCSClassInternal(LuaState lua, IScriptObject? obj, [DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type? specifyType = null)
	{
		if (!lua.IsAlive)
		{
			PT.PrintErr("LuaState state is dead");
			return;
		}

		object objKey = (object?)obj ?? specifyType!;
		Type type = specifyType ?? obj!.GetType();
		bool isValueType = type.IsValueType;

		if (!isValueType)
		{
			// Non-value types, check weak cache
			string regKey = GetRegKeyFromObj(objKey);
			if (GetFromWeakCache(lua, regKey))
				return;

			PushNewUserdata(lua, objKey);
			ApplyMetatable(lua, type);
			SetInWeakCache(lua, regKey);
		}
		else
		{
			// Value types, push fresh userdata but reuse metatable
			PushNewUserdata(lua, objKey);
			ApplyMetatable(lua, type);
		}

		// Force collect garbage after allocs
		if (++_allocsSinceLastGC >= GCStepThreshold)
		{
			lua.GarbageCollector(LuaGC.Collect, 1);
			_allocsSinceLastGC = 0;
		}
	}

	private static void PushNewUserdata(LuaState lua, object objKey)
	{
		GCHandle handle = GCHandle.Alloc(objKey);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = lua.NewUserDataDTor((UIntPtr)IntPtr.Size, GarbageCollect);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);
		_ptrToObject.Add(handlePtr, objKey);
	}

	private void ApplyMetatable(LuaState lua, [DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type type)
	{
		string metatableKey = "__metatable_" + type.Name;

		lua.GetField(LuaState.LUA_REGISTRYINDEX, metatableKey);
		if (lua.Type(-1) == LuaType.Nil)
		{
			lua.Pop(1);
			lua.NewTable();

			LuaMetatable metatable = new()
			{
				Lua = lua,
				TargetType = type,
				LangProvider = this,
			};

			metatable.RegisterMetamethods();

			if (!metatable.HasCustomIndex)
			{
				lua.PushCFunction(metatable.IndexWrapper, "__index");
				lua.SetField(-2, "__index");
			}

			if (!metatable.HasCustomNewIndex)
			{
				lua.PushCFunction(metatable.NewIndexWrapper, "__newindex");
				lua.SetField(-2, "__newindex");
			}

			if (!metatable.HasToString)
			{
				lua.PushCFunction(metatable.ToString, "__tostring");
				lua.SetField(-2, "__tostring");
			}

			lua.PushCFunction(metatable.NameCallWrapper, "__namecall");
			lua.SetField(-2, "__namecall");

			lua.PushString(ProcessTypeName(type));
			lua.SetField(-2, "__type");

			lua.PushBoolean(false);
			lua.SetField(-2, "__metatable");

			// Store in metatable cache
			lua.PushValue(-1);
			lua.SetField(LuaState.LUA_REGISTRYINDEX, metatableKey);
		}

		lua.SetMetaTable(-2);
	}

	private static string ProcessTypeName(Type type)
	{
		if (type.IsAssignableTo(typeof(IScriptGDObject)))
		{
			return type.Name.TrimPrefix("PT");
		}
		return type.Name;
	}

	public void FreePTCallback(PTCallback action)
	{
		PTCallbackData? data = _ptrToCallback.Values.FirstOrDefault(data => data.Callback == action);
		if (data.HasValue)
		{
			PTCallbackData callbackData = data.Value;
			LuaState lua = callbackData.State;
			lua.Unref(callbackData.RefID);
			lua.Unref(callbackData.HandlerRefID);
			_ptrToCallback.Remove(callbackData.FuncPtr);
			_callbackToPtr.Remove(callbackData);
		}
	}

	public static void InitializeCache(LuaState lua)
	{
		lua.NewTable();

		lua.NewTable();
		lua.PushString("v");
		lua.SetField(-2, "__mode"); // Set __mode = "v"

		lua.SetMetaTable(-2);

		lua.SetField(LuaState.LUA_REGISTRYINDEX, WeakUserdataCache);
	}

	private static bool GetFromWeakCache(LuaState lua, string regKey)
	{
		lua.GetField(LuaState.LUA_REGISTRYINDEX, WeakUserdataCache);

		lua.GetField(-1, regKey);

		if (lua.Type(-1) != LuaType.Nil)
		{
			lua.Replace(-2);
			return true;
		}

		lua.Pop(2);
		return false;
	}

	private static void SetInWeakCache(LuaState lua, string regKey)
	{
		lua.GetField(LuaState.LUA_REGISTRYINDEX, WeakUserdataCache);
		lua.PushValue(-2);
		lua.SetField(-2, regKey);
		lua.Pop(1);
	}

	private static void SetGlobalTablePtr<T>(LuaState state, nint ptr, T value) where T : class
	{
		state.PushLightUserData(ptr);
		state.PushObject(value);
		state.SetTable(LuaState.LUA_GLOBALSINDEX);
	}

	private static T? GetGlobalTablePtr<T>(LuaState state, nint ptr) where T : class
	{
		state.PushLightUserData(ptr);
		state.GetTable(LuaState.LUA_GLOBALSINDEX);
		var result = state.ToObject(-1) as T;
		state.Pop(1);
		return result;
	}

	public static Script GetScriptInstance(LuaState state)
	{
		return GetGlobalTablePtr<Script>(state, _internalScriptPtr)!;
	}

	public static LogDispatcher GetLogger(LuaState state)
	{
		return GetGlobalTablePtr<LogDispatcher>(state, _loggerPtr)!;
	}

	public void Dispose() { }

	private readonly struct MethodsCacheKey(Type type, string methodName, bool isCompatibility) : IEquatable<MethodsCacheKey>
	{
		public readonly Type Type = type;
		public readonly string MethodName = methodName;
		public readonly bool IsCompatibility = isCompatibility;

		public bool Equals(MethodsCacheKey other)
		{
			return Type == other.Type &&
				   MethodName.Equals(other.MethodName, StringComparison.CurrentCultureIgnoreCase) &&
				   IsCompatibility == other.IsCompatibility;
		}

		public override bool Equals(object? obj)
		{
			return obj is MethodsCacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(
				Type,
				MethodName.ToLowerInvariant(),
				IsCompatibility
			);
		}
	}

	private struct PTCallbackData : IEquatable<PTCallbackData>
	{
		public int RefID { get; set; }
		public int HandlerRefID { get; set; }
		public IntPtr FuncPtr { get; set; }
		public PTCallback Callback { get; set; }
		public LuaState State { get; set; }

		public readonly bool Equals(PTCallbackData other)
		{
			return other is PTCallbackData otherData && FuncPtr.Equals(otherData.FuncPtr);
		}

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(FuncPtr, RefID, Callback);
		}

		public override readonly bool Equals(object? obj)
		{
			return obj is PTCallbackData otherData && FuncPtr.Equals(otherData.FuncPtr);
		}
	}

	private struct ThreadData
	{
		public Task<int> Task { get; set; }
		public TaskCompletionSource<int> TaskSource { get; set; }
	}

	private struct ScriptThreadData
	{
		public Script Script;
	}
}
