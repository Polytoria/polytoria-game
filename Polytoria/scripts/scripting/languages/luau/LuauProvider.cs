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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting.Luau;

public sealed partial class LuauProvider : IScriptLanguageProvider
{
	private const DynamicallyAccessedMemberTypes DynamicallyAccessedTypes = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods;
	private const int GCStepThreshold = 100;

	private static readonly Dictionary<Type, MethodInfo?> _gdToProxy = [];
	private readonly ConcurrentDictionary<IntPtr, PTCallbackData> _ptrToCallback = [];
	private readonly ConcurrentDictionary<PTCallback, IntPtr> _callbackPointers = [];
	private static readonly Type[] _instantiatableTypes;
	private const string WeakUserdataCache = "__UDCACHE";

	private static readonly ConditionalWeakTable<object, string> _objectIDS = new();
	private static readonly ConditionalWeakTable<Script, UpdateState> _updateStates = new();
	private static long _nextObjectID;

	private int _allocsSinceLastGC;

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

	static LuauProvider()
	{
		foreach ((Type target, Type proxy) in ScriptService.ProxyMap)
		{
			RegisterProxy(target, proxy);
		}

#pragma warning disable IL2026
		_instantiatableTypes = Assembly.GetExecutingAssembly().GetTypes()
			.Where(type => type.IsDefined(typeof(InstantiableAttribute), false))
			.ToArray();
#pragma warning restore IL2026
	}

	public LuauProvider()
	{
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
		LuaState state;
		try
		{
			state = InitalizeScript(script);
			script.TryCompile();
			state.Load(script.LuaPath, script.Bytecode!);
		}
		catch (Exception e)
		{
			if (script.LuauState != null) Close(script);
			script.Root.ScriptService.Logger.LogError(script, e.Message);
			return;
		}

		_ = ResumeThread(state, null, 0, isMainThread: true);
	}

	public byte[] CompileSource(string source)
	{
		return LuaState.Compile(source);
	}

	public LuaState InitalizeScript(Script script)
	{
		if (script.LuauCancellation.IsCancellationRequested)
		{
			script.LuauCancellation = new();
		}
		LuaState state = NewReferencedThread(GlobalLuaState, out int stateRef);
		lock (script.LuauReferencesLock)
			script.LuauFunctionRefs.Add(stateRef);
		script.LuauState = state;

		state.SandboxGlobals();

		// Internal script
		SetGlobalTablePtr(state, _internalScriptPtr, script);
		SetGlobalTablePtr(state, _loggerPtr, script.Root.ScriptService.Logger);

		state.Register("print", LuaPrint);
		state.Register("warn", LuaWarn);
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

		// Expose all instantiatables
		foreach (Type type in _instantiatableTypes)
		{
#pragma warning disable IL2072
			PushCSClass(state, type);
#pragma warning restore IL2072 // Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.
			state.SetGlobal(type.Name);
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

		var mainThread = NewReferencedThread(state, out int mainThreadRef);
		lock (script.LuauReferencesLock)
			script.LuauFunctionRefs.Add(mainThreadRef);
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

			co = NewReferencedThread(mainThread, out coRef);

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
			GlobalLuaState.TryUnref(coRef);
		}
	}

	public void Close(Script script)
	{
		if (script.LuauState == null) return;
		_updateStates.Remove(script);
		script.Ran = false;
		script.LuauCancellation.Cancel();

		// Free function pointer references
		IntPtr[] functionPointers;
		int[] functionRefs;
		lock (script.LuauReferencesLock)
		{
			functionPointers = script.LuauFunctionPointers.ToArray();
			functionRefs = script.LuauFunctionRefs.ToArray();
			script.LuauFunctionPointers.Clear();
			script.LuauFunctionRefs.Clear();
		}

		foreach (IntPtr funcPtr in functionPointers)
		{
			// Only the caller that removes the callback releases its references.
			if (_ptrToCallback.TryRemove(funcPtr, out PTCallbackData func))
			{
				func.Callback.Dispose();
				GlobalLuaState.TryUnref(func.RefID);
				GlobalLuaState.TryUnref(func.HandlerRefID);
				_callbackPointers.TryRemove(func.Callback, out _);
			}
		}
		foreach (int reference in functionRefs)
		{
			GlobalLuaState.TryUnref(reference);
		}
		if (script is ModuleScript module)
		{
			if (module.CachedLuauResultRef.HasValue)
			{
				GlobalLuaState.TryUnref(module.CachedLuauResultRef.Value);
				module.CachedLuauResultRef = null;
			}
			module.LuauLoadError = "Module was closed";
			module.LuauLoadCompletion?.TrySetResult(false);
			module.LuauLoadCompletion = null;
			module.LuauLoadState = ModuleLoadState.NotLoaded;
			module.LuauWaitingFor = null;
			module.LuauLoadedSource = null;
		}

		PTSignal.CleanupScript(script);
		script.LuauMainThread?.Dispose();
		script.LuauState.Dispose();
		script.LuauMainThread = null;
		script.LuauState = null;
	}

	public void CallUpdate(Script script, double delta)
	{
		if (script.LuauMainThread == null || !script.LuauMainThread.IsAlive) return;
		string updateKeyword = "_Update";

		if (script.Compatibility)
		{
			updateKeyword = "_update";
		}

		StartUpdate(script, updateKeyword, delta, fixedUpdate: false);
	}

	public void CallFixedUpdate(Script script, double delta)
	{
		if (script.LuauMainThread == null || !script.LuauMainThread.IsAlive) return;
		string updateKeyword = "_FixedUpdate";

		if (script.Compatibility)
		{
			updateKeyword = "_fixed_update";
		}

		StartUpdate(script, updateKeyword, delta, fixedUpdate: true);
	}

	private void StartUpdate(Script script, string functionName, double delta, bool fixedUpdate)
	{
		UpdateState state = _updateStates.GetOrCreateValue(script);
		ref int running = ref fixedUpdate ? ref state.FixedUpdateRunning : ref state.UpdateRunning;
		if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
		{
			return;
		}

		_ = RunUpdateAsync(script, functionName, delta, state, fixedUpdate);
	}

	private async Task RunUpdateAsync(Script script, string functionName, double delta, UpdateState state, bool fixedUpdate)
	{
		try
		{
			await CallAsync(script, functionName, [delta]);
		}
		catch (Exception ex)
		{
			script.Root.ScriptService.Logger.LogError(script, ex.InnerException?.Message ?? ex.Message);
		}
		finally
		{
			if (fixedUpdate)
			{
				Volatile.Write(ref state.FixedUpdateRunning, 0);
			}
			else
			{
				Volatile.Write(ref state.UpdateRunning, 0);
			}
		}
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

	private static LuaState NewReferencedThread(LuaState from, out int reference)
	{
		lock (from)
		{
			LuaState thread = NewThread(from);
			reference = from.Ref();
			return thread;
		}
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

				if (!script.ShouldContinue) return;

				LuaStatus status;
				if (!thread.TryResume(from, narg, out status))
				{
					break;
				}
				from = null;

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
		catch (OperationCanceledException) when (script.LuauCancellation.IsCancellationRequested)
		{
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
			if (script.LanguageProvider is LuauProvider provider)
				provider.GlobalLuaState.TryUnref(threadRef);
			else
				thread.TryUnref(threadRef);
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
	public static int LuaWarn(IntPtr L)
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
		logger.LogWarning(script, logInfo);
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

		TaskCompletionSource<int> tcs = NewCompletionSource();

		SetYieldTask(lua, tcs.Task);

		async Task RunAsync()
		{
			try
			{
				if (n != 0)
				{
					await Globals.Singleton.WaitAsync((float)n).WaitAsync(GetScriptInstance(lua).LuauCancellation.Token);
				}
				else
				{
					await Globals.Singleton.WaitPhysicsFrame().WaitAsync(GetScriptInstance(lua).LuauCancellation.Token);
				}

				bool pushed = lua.TryAccessYielded(() => PushValueToLua(lua, true));
				tcs.TrySetResult(pushed ? 1 : 0);
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
			}
		}

		_ = RunAsync();

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

		if (ms.LuauLoadedSource != null && !string.Equals(ms.LuauLoadedSource, source, StringComparison.Ordinal))
		{
			Close(ms);
			ms.Bytecode = null;
		}

		// Return existing if already required
		if (ms.LuauLoadState == ModuleLoadState.Loaded && ms.CachedLuauResultRef.HasValue)
		{
			state.GetRef(ms.CachedLuauResultRef.Value);
			return 1;
		}

		if (ms.LuauLoadState == ModuleLoadState.Loading && ms.LuauLoadCompletion != null)
		{
			if (WouldCreateRequireCycle(script as ModuleScript, ms))
				return state.Error("circular module dependency detected");
			if (script is ModuleScript waitingModule) waitingModule.LuauWaitingFor = ms;
			TaskCompletionSource<int> pendingTcs = NewCompletionSource();
			SetYieldTask(state, pendingTcs.Task);
			_ = HandlePendingRequireAsync(state, ms, script as ModuleScript, pendingTcs);
			return state.Yield(1);
		}

		if (WouldCreateRequireCycle(script as ModuleScript, ms))
			return state.Error("circular module dependency detected");
		if (script is ModuleScript requiringModule) requiringModule.LuauWaitingFor = ms;

		ms.LuauLoadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ms.LuauLoadError = null;
		ms.LuauLoadState = ModuleLoadState.Loading;
		ms.LuauLoadedSource = source;

		LuaState co = InitalizeScript(ms);

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
			ResetFailedModule(ms, capturedException1.Message);
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
			ResetFailedModule(ms, capturedException2.Message);
			return state.Error(co.ToString(-1) ?? capturedException2.Message);
		}

		TaskCompletionSource<int> tcs = NewCompletionSource();
		SetYieldTask(state, tcs.Task);

		_ = HandleRequireAsync(co, state, chunkName, script as ModuleScript, tcs, ms);

		return state.Yield(1);
	}

	private async Task HandleRequireAsync(LuaState co, LuaState state, string chunkName, ModuleScript? caller, TaskCompletionSource<int> tcs, ModuleScript ms)
	{
		try
		{
			await ResumeThread(co, state, 0, true);
			await Task.Yield();

			if (co.GetTop() == 0 || co.IsNil(1))
				throw new Exception("module must return a value");

			const int resultCount = 1;
			co.PushValue(1);
			ms.CachedLuauResultRef = co.Ref();
			ms.LuauLoadState = ModuleLoadState.Loaded;

			bool delivered = state.TryAccessYielded(() =>
			{
				co.PushValue(1);
				co.XMove(state, 1);
			});
			ms.LuauLoadCompletion?.TrySetResult(true);
			ms.LuauLoadCompletion = null;
			tcs.TrySetResult(delivered ? resultCount : 0);
		}
		catch (Exception ex)
		{
			ResetFailedModule(ms, ex.Message);
			tcs.TrySetException(new Exception($"Failure when requiring {chunkName}: {ex.Message}"));
		}
		finally
		{
			if (caller != null && ReferenceEquals(caller.LuauWaitingFor, ms)) caller.LuauWaitingFor = null;
		}
	}

	private static async Task HandlePendingRequireAsync(LuaState state, ModuleScript module, ModuleScript? caller, TaskCompletionSource<int> tcs)
	{
		try
		{
			TaskCompletionSource<bool> completion = module.LuauLoadCompletion!;
			await completion.Task;

			if (module.LuauLoadState == ModuleLoadState.Loaded && module.CachedLuauResultRef.HasValue)
			{
				bool delivered = state.TryAccessYielded(() => state.GetRef(module.CachedLuauResultRef.Value));
				tcs.TrySetResult(delivered ? 1 : 0);
			}
			else
			{
				tcs.TrySetException(new Exception(module.LuauLoadError ?? "Module failed to load"));
			}
		}
		catch (Exception ex)
		{
			tcs.TrySetException(ex);
		}
		finally
		{
			if (caller != null && ReferenceEquals(caller.LuauWaitingFor, module)) caller.LuauWaitingFor = null;
		}
	}

	private static bool WouldCreateRequireCycle(ModuleScript? caller, ModuleScript target)
	{
		if (caller == null) return false;

		HashSet<ModuleScript> visited = [];
		ModuleScript? current = target;
		while (current != null && visited.Add(current))
		{
			if (ReferenceEquals(current, caller)) return true;
			current = current.LuauWaitingFor;
		}
		return false;
	}

	private static TaskCompletionSource<int> NewCompletionSource()
	{
		return new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private void ResetFailedModule(ModuleScript module, string error)
	{
		module.LuauLoadCompletion?.TrySetResult(false);
		if (module.LuauState != null)
		{
			Close(module);
		}
		else
		{
			module.LuauLoadCompletion = null;
			module.LuauLoadState = ModuleLoadState.NotLoaded;
		}
		module.LuauLoadError = error;
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

		LuaState co = NewReferencedThread(state, out int coRef);

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

		Script script = GetScriptInstance(state);
		LuaState registry = (script.LanguageProvider as LuauProvider)?.GlobalLuaState ?? state;
		_ = RunSpawnedThreadAsync(co, registry, coRef, numArgs);

		return 0;
	}

	private static async Task RunSpawnedThreadAsync(LuaState thread, LuaState registry, int reference, int argumentCount)
	{
		try
		{
			await ResumeThread(thread, null, argumentCount);
		}
		finally
		{
			registry.TryUnref(reference);
		}
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
		LuaState co = NewReferencedThread(state, out int coRef);

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

		TaskCompletionSource<int> tcs = NewCompletionSource();
		SetYieldTask(state, tcs.Task);

		_ = HandlePCallAsync(co, state, funcRef, coRef, nargs, tcs);

		return state.Yield(2);
	}

	private async Task HandlePCallAsync(LuaState co, LuaState state, int funcRef, int coRef, int nargs, TaskCompletionSource<int> tcs)
	{
		try
		{
			await ResumeThread(co, state, nargs, true);
			await Task.Yield();
			int nresults = co.GetTop();
			bool delivered = state.TryAccessYielded(() =>
			{
				PushValueToLua(state, true);
				if (nresults > 0) co.XMove(state, nresults);
			});
			tcs.TrySetResult(delivered ? 1 + nresults : 0);
		}
		catch (Exception ex)
		{
			bool delivered = state.TryAccessYielded(() =>
			{
				PushValueToLua(state, false);
				PushValueToLua(state, ex.InnerException?.Message ?? ex.Message);
			});
			tcs.TrySetResult(delivered ? 2 : 0);
		}
		finally
		{
			GlobalLuaState.TryUnref(funcRef);
			GlobalLuaState.TryUnref(coRef);
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

		LuaState thread = lua.ToThread(1);
		int narg = lua.GetTop() - 1;
		lua.XMove(thread, narg);

		LuaStatus status = ResumeThreadDirect(thread, lua, narg, out ThreadData? threadData);

		if (status == LuaStatus.OK || status == LuaStatus.Break)
		{
			lua.PushBoolean(true);

			int nresults = thread.GetTop();
			if (nresults > 0)
			{
				thread.XMove(lua, nresults);
			}

			return 1 + nresults;
		}
		else if (status == LuaStatus.Yield)
		{
			if (threadData.HasValue)
			{
				_ = HandleYieldTaskAsync(thread, lua, threadData.Value.Task);
				lua.PushBoolean(true);
				return 1;
			}
			else
			{
				lua.PushBoolean(true);

				int nresults = thread.GetTop();
				if (nresults > 0)
				{
					thread.XMove(lua, nresults);
				}

				return 1 + nresults;
			}
		}
		else
		{
			string errorMessage = thread.ToString(-1) ?? "cannot resume coroutine";
			lua.PushBoolean(false);
			lua.PushString(errorMessage);

			return 2;
		}
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

		lua.PushCFunction(AuxCoroutineWrap, n: 1);

		return 1;
	}

	private static int AuxCoroutineWrap(IntPtr L)
	{
		LuaState lua = LuaState.FromIntPtr(L);

		LuaState thread = lua.ToThread(LuaState.UpValIndex(1));
		int narg = lua.GetTop();
		lua.XMove(thread, narg);

		LuaStatus status = ResumeThreadDirect(thread, lua, narg, out ThreadData? threadData);

		if (status == LuaStatus.OK || status == LuaStatus.Break)
		{
			int nresults = thread.GetTop();
			if (nresults > 0)
			{
				thread.XMove(lua, nresults);
			}

			return nresults;
		}
		else if (status == LuaStatus.Yield)
		{
			if (threadData.HasValue)
			{
				_ = HandleYieldTaskAsync(thread, lua, threadData.Value.Task);
				return 0;
			}
			else
			{
				int nresults = thread.GetTop();
				if (nresults > 0)
				{
					thread.XMove(lua, nresults);
				}

				return nresults;
			}
		}
		else
		{
			return lua.Error(thread.ToString(-1) ?? "cannot resume coroutine");
		}
	}

	private static LuaStatus ResumeThreadDirect(LuaState thread, LuaState from, int narg, out ThreadData? threadData)
	{
		LuaStatus status;
		lock (thread)
		{
			status = thread.Resume(from, narg);
		}

		if (status == LuaStatus.Yield)
		{
			lock (thread)
			{
				threadData = GetThreadData(thread);
			}
		}
		else
		{
			threadData = null;
		}

		return status;
	}

	private static async Task HandleYieldTaskAsync(LuaState thread, LuaState from, Task<int> initialTask)
	{
		await ResumeThread(thread, null, await initialTask, false);
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
#pragma warning disable IL2072
			PushEnum(state, valType, numericValue);
#pragma warning restore IL2072
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

			if (obj is sbyte or byte or short or ushort or int or uint or long or ulong)
			{
				return obj;
			}
		}
		else if (state.IsFunction(index) && getAsFunction) // PTFunction
		{
			Script script = GetScriptInstance(state);

			state.PushValue(index);
			int funcRef = state.Ref();
			lock (script.LuauReferencesLock)
				script.LuauFunctionRefs.Add(funcRef);

			LuaState mainState = script.LuauState ?? throw new Exception("INTERNAL BUG: No main thread");

			PTFunction del = new(async (args) =>
			{
				if (!mainState.IsAlive) return [];

				LuaState co;
				int coRef;

				lock (mainState)
				{
					co = NewReferencedThread(mainState, out coRef);

					mainState.GetRef(funcRef);
					mainState.XMove(co, 1);

					foreach (object? arg in args)
					{
						PushValueToLua(co, arg);
					}
				}

				await ResumeThread(co, null, args.Length, true);

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
					GlobalLuaState.TryUnref(coRef);
				}
			})
			{
				LangProvider = this,
				FromScript = script,
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

			LuaState handler = NewReferencedThread(mainState, out int handlerRef);

			PTCallback del = new(async (args) =>
			{
				if (!mainState.IsAlive) return;

				LuaState co;
				int coRef;

				co = NewReferencedThread(handler, out coRef);

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
					GlobalLuaState.TryUnref(coRef);
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
			_callbackPointers[del] = funcPtr;
			lock (script.LuauReferencesLock)
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
									object? key = LuaToObjectInternal(state, -2, convertToGD, false, visitedTables);
									if (key != null)
										otherKeyed[key] = LuaToObjectInternal(state, -1, convertToGD, false, visitedTables);
								}
							}
							else
							{
								object? key = LuaToObjectInternal(state, -2, convertToGD, false, visitedTables);
								if (key != null)
									otherKeyed[key] = LuaToObjectInternal(state, -1, convertToGD, false, visitedTables);
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

	public void PushEnumTable(LuaState lua, [DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type specifyType)
	{
		lua.NewTable();
		foreach (string item in Enum.GetNames(specifyType))
		{
			object value = Enum.Parse(specifyType, item);
			Type underlyingType = Enum.GetUnderlyingType(specifyType);
			PushEnum(lua, specifyType, Convert.ChangeType(value, underlyingType));
			lua.SetField(-2, item);
		}
	}

	private static void GarbageCollect(IntPtr ud)
	{
		IntPtr handlePtr = Marshal.ReadIntPtr(ud);
		if (handlePtr == IntPtr.Zero) return;

		GCHandle handle = GCHandle.FromIntPtr(handlePtr);
		if (handle.IsAllocated)
			handle.Free();
	}

	public void PushEnum(LuaState lua, [DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type specifyType, object value)
	{
		Script script = GetScriptInstance(lua);

		GCHandle handle = GCHandle.Alloc(value);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = lua.NewUserDataDTor((UIntPtr)IntPtr.Size, GarbageCollect);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);

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
		return _objectIDS.GetValue(
			obj,
			_ => "__userdata_" + Interlocked.Increment(ref _nextObjectID)
		);
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
		if (Interlocked.Increment(ref _allocsSinceLastGC) >= GCStepThreshold)
		{
			lua.GarbageCollector(LuaGC.Step, 1);
			Interlocked.Exchange(ref _allocsSinceLastGC, 0);
		}
	}

	private static void PushNewUserdata(LuaState lua, object objKey)
	{
		GCHandle handle = GCHandle.Alloc(objKey);
		IntPtr handlePtr = GCHandle.ToIntPtr(handle);
		IntPtr userdataPtr = lua.NewUserDataDTor((UIntPtr)IntPtr.Size, GarbageCollect);
		Marshal.WriteIntPtr(userdataPtr, handlePtr);
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
		if (_callbackPointers.TryRemove(action, out IntPtr funcPtr)
			&& _ptrToCallback.TryRemove(funcPtr, out PTCallbackData callbackData))
		{
			GlobalLuaState.TryUnref(callbackData.RefID);
			GlobalLuaState.TryUnref(callbackData.HandlerRefID);
			Script? script = callbackData.Callback.FromScript;
			if (script != null)
			{
				lock (script.LuauReferencesLock)
					script.LuauFunctionPointers.Remove(callbackData.FuncPtr);
			}
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

	public void Dispose()
	{
		foreach (PTCallbackData callback in _ptrToCallback.Values)
		{
			callback.Callback.Dispose();
		}
		_ptrToCallback.Clear();
		_callbackPointers.Clear();
		GlobalLuaState?.Dispose();
	}

	private readonly struct MethodsCacheKey(Type type, string methodName, bool isCompatibility) : IEquatable<MethodsCacheKey>
	{
		public readonly Type Type = type;
		public readonly string MethodName = methodName;
		public readonly bool IsCompatibility = isCompatibility;

		public bool Equals(MethodsCacheKey other)
		{
			return Type == other.Type &&
				   MethodName.Equals(other.MethodName, StringComparison.OrdinalIgnoreCase) &&
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
				StringComparer.OrdinalIgnoreCase.GetHashCode(MethodName),
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

	private sealed class UpdateState
	{
		public int UpdateRunning;
		public int FixedUpdateRunning;
	}
}
