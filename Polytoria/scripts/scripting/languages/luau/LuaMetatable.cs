// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Services;
using Polytoria.Scripting.Datatypes;
using Polytoria.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting.Luau;

public class LuaMetatable : LuaObject
{
	private const string VectorPrelude = """
		local magnitude = vector.magnitude
		local dot = vector.dot
		local cross = vector.cross
		local create = vector.create
		local atan2 = math.atan2
		local floor = math.floor
		local zero = create(0, 0, 0)

		local function f32(x)
			return create(x, 0, 0).x
		end

		local epsilon = f32(1e-06)

		local function normalize(v)
			local m = magnitude(v)
			if m == 0 then
				return zero
			end
			return v / m
		end

		local function angle(a, b)
			return f32(atan2(magnitude(cross(a, b)), dot(a, b)))
		end

		local function lerp(a, b, t)
			return a + (b - a) * t
		end

		local function round(x)
			local r = floor(x + 0.5)
			if r - x == 0.5 and r % 2 ~= 0 then
				r -= 1
			end
			if r == 0 and (x < 0 or 1 / x < 0) then
				return -0.0
			end
			return r
		end

		local index = ...

		local methods = {
			Dot = dot,
			Cross = cross,
			Floor = vector.floor,
			Ceil = vector.ceil,
			Abs = vector.abs,
			Sign = vector.sign,
			Min = vector.min,
			Max = vector.max,
			Normalize = normalize,
			Lerp = lerp,
			Reflect = function(v, normal)
				return normal * (2 * dot(v, normal)) - v
			end,
			Distance = function(a, b)
				return magnitude(b - a)
			end,
			SqrDistance = function(a, b)
				local d = b - a
				return dot(d, d)
			end,
			Angle = angle,
			SignedAngle = function(from, to, axis)
				local unsigned = angle(from, to)
				if dot(cross(from, to), axis) < 0 then
					return -unsigned
				end
				return unsigned
			end,
			MoveTowards = function(current, target, maxDistanceDelta)
				local delta = f32(maxDistanceDelta)
				local vd = target - current
				local len = magnitude(vd)
				if len <= delta or len < epsilon then
					return target
				end
				return current + vd / len * delta
			end,
			Project = function(v, onNormal)
				return onNormal * f32(dot(v, onNormal) / dot(onNormal, onNormal))
			end,
			ProjectOnPlane = function(v, planeNormal)
				local n = normalize(planeNormal)
				return v - n * dot(v, n)
			end,
			Round = function(v)
				return create(round(v.x), round(v.y), round(v.z))
			end,
			LimitLength = function(v, length)
				length = f32(length)
				local l = magnitude(v)
				if l > 0 and length < l then
					return v / l * length
				end
				return v
			end,
		}

		return function(v, key)
			if key == "Magnitude" then
				return magnitude(v)
			elseif key == "Normalized" then
				return normalize(v)
			elseif key == "SqrMagnitude" then
				return dot(v, v)
			end
			return methods[key] or index(v, key)
		end, methods
		""";

	private static readonly Dictionary<PropertyInfo, PropertyAccessData> _propertyAccessCache = [];
	private static readonly Dictionary<MethodInfo, MethodInvocationData> _methodInvocationCache = [];

	private readonly Dictionary<(string, bool), int> _staticFunctionRefs = [];
	private MemberEntry?[] _members = [];
	private MemberEntry?[] _legacyMembers = [];
	private readonly Dictionary<(string, bool), MemberEntry> _namedMembers = [];

	public LuaState Lua = null!;
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
	public Type TargetType = null!;
	public LuauProvider LangProvider = null!;

	public bool HasCustomIndex = false;
	public bool HasCustomNewIndex = false;
	public bool HasToString = false;
	public bool HasCustomNameCall = false;

	private async void AsyncExec(Script script, object? targetObject, LuaState state, MethodInfo targetMethod, Func<object?, object?[], object?>? invoker, object?[] args, TaskCompletionSource<int> tcs)
	{
		object invokeResult;
		try
		{
			invokeResult = (script.Compatibility
				? targetMethod.Invoke(targetObject, args)
				: (invoker ?? throw new InvalidOperationException($"{targetMethod.Name} has no generated invoker"))(targetObject, args))!;
		}
		catch (Exception ex)
		{
			tcs.TrySetException(ex.InnerException ?? ex);
			return;
		}

		if (invokeResult is Task task)
		{
			try
			{
				await task;
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
				return;
			}

			if (!script.ShouldContinue)
			{
				tcs.TrySetResult(0);
				return;
			}
			Type returnType = targetMethod.ReturnType;

			if (returnType == typeof(Task))
			{
				tcs.TrySetResult(0);
				return;
			}

			// --------------- HANDLE TASK ---------------
			if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
			{
				try
				{
					if (task is Task<int> intTask)
					{
						LangProvider.PushValueToLua(state, intTask.Result);
					}
					else if (task is Task<string> stringTask)
					{
						LangProvider.PushValueToLua(state, stringTask.Result);
					}
					else if (task is Task<bool> boolTask)
					{
						LangProvider.PushValueToLua(state, boolTask.Result);
					}
					else if (task is Task<object> objectTask)
					{
						LangProvider.PushValueToLua(state, objectTask.Result);
					}
					else if (task is Task<Instance> instanceTask)
					{
						LangProvider.PushValueToLua(state, instanceTask.Result);
					}
					else if (task is Task<NetworkedObject> netObjTask)
					{
						LangProvider.PushValueToLua(state, netObjTask.Result);
					}
					else if (task is Task<Clothing> clothingTask)
					{
						LangProvider.PushValueToLua(state, clothingTask.Result);
					}
					else if (task is Task<Accessory> accessoryTask)
					{
						LangProvider.PushValueToLua(state, accessoryTask.Result);
					}
					else if (task is Task<Tool> toolTask)
					{
						LangProvider.PushValueToLua(state, toolTask.Result);
					}
					else if (task is Task<HttpResponseData> responseTask)
					{
						LangProvider.PushValueToLua(state, responseTask.Result);
					}
					else if (task is Task<byte[]> byteArrayTask)
					{
						LangProvider.PushValueToLua(state, byteArrayTask.Result);
					}
					else if (task is Task<object?[]> objectArrayTask)
					{
						foreach (object? r in objectArrayTask.Result)
						{
							LangProvider.PushValueToLua(state, r);
						}
						tcs.TrySetResult(objectArrayTask.Result.Length);
						return;
					}
					else
					{
						throw new NotSupportedException($"INTERNAL BUG: Task type {task.GetType()} is not supported in AOT");
					}
					tcs.TrySetResult(1);
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex.InnerException ?? ex);
				}
			}
		}
		else
		{
			tcs.TrySetResult(0);
		}
	}

	public virtual int Index(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		Script script = LuauProvider.GetScriptInstance(state);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		string? key = null;
		int atom = -1;

		if (targetObject is Instance instance)
		{
			LuaType t = state.Type(2);

			// Index child
			Instance? child = null;

			if (t == LuaType.String)
			{
				string? indexK = key = state.ToAtomString(2, out atom);

				if (indexK != null)
				{
					if (script.Compatibility)
					{
						child = instance.LegacyFindChild(indexK);
					}
					else
					{
						child = instance.FindChild(indexK);
					}
				}
			}
			else if (t == LuaType.Number)
			{
				int indexK = (int)state.ToNumber(2) - 1;
				child = instance.FindChildByIndex(indexK);
			}

			if (child != null)
			{
				LangProvider.PushValueToLua(state, child);
				return 1;
			}
		}

		// stack[1] = table
		// stack[2] = key
		key ??= state.ToAtomString(2, out atom);

		if (key == null) { return 0; }

		// Call/Retrieve other script's property/functions
		if (targetObject is Script scriptref && targetObject.GetType() == script.GetType())
		{
			if (scriptref.Ran && scriptref.LuauMainThread != null && scriptref.ShouldContinue)
			{
				LuaState targetState = scriptref.LuauMainThread;
				targetState.GetGlobal(key);

				if (targetState.IsNil(-1))
				{
					targetState.Pop(1);
				}
				else
				{
					targetState.XMove(state, 1);
					return 1;
				}
			}
		}

		// Handle Object.New for NetworkedObjects
		if (key == "New")
		{
			if (TargetType.IsDefined(typeof(InstantiableAttribute)))
			{
				if (TryPushStaticFunction(state, key, script.Compatibility))
					return 1;

				int newFunc(IntPtr L)
				{
					LuaState state = LuaState.FromIntPtr(L);
					Script script = LuauProvider.GetScriptInstance(state);
					NetworkedObject? parentTo = null;
					object? parentObj = LangProvider.LuaToObject(state, 1);

					if (parentObj is NetworkedObject p)
					{
						parentTo = p;
					}

					LangProvider.PushValueToLua(state, NetworkedObject.NewFromScript(script, TargetType.Name, parentTo));
					return 1;
				}

				int safeNewFunc(IntPtr L)
				{
					Exception? caughtException;

					try
					{
						return newFunc(L);
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

				return PushStaticFunction(state, key, script.Compatibility, safeNewFunc);
			}
		}

		MemberEntry member = GetMember(key, atom, script.Compatibility);
		PropertyInfo? prop = member.Property;
		ScriptService.MethodsCacheData methodInfos = member.Methods;
		MethodInfo? method = methodInfos.Methods.Length > 0 ? methodInfos.Methods[0] : null;

		if (method != null && member.HandlesLuaState)
		{
			return (int)method.Invoke(targetObject, [state])!;
		}

		if (prop != null)
		{
			PropertyAccessData accessData = member.Access;
			if (accessData.Permissions != ScriptPermissionFlags.None &&
				!script.PermissionFlags.HasFlag(accessData.Permissions))
			{
				throw new UnauthorizedAccessException("script does not have permission to access the specified property (" + key + ")");
			}

			if (accessData.ObsoleteMessage != null)
			{
				PT.PrintWarn($"{prop.Name} is obsolete. {accessData.ObsoleteMessage}");
			}

			object? value = script.Compatibility
				? prop.GetValue(targetObject)
				: (accessData.Getter ?? throw new InvalidOperationException($"{prop.Name} has no generated getter"))(targetObject!);
			LangProvider.PushValueToLua(state, value);

			return 1;
		}
		else if (method != null)
		{
			bool isStatic = targetObject == null;
			if (isStatic && TryPushStaticFunction(state, key, script.Compatibility))
				return 1;

			LuaFunction func = NewMethodFunction(key, member, targetObject, script);

			if (isStatic)
				return PushStaticFunction(state, key, script.Compatibility, func);

			state.PushCFunction(func, key);
			return 1;
		}
		else
		{
			//GD.PushWarning(script.LuaPath, " couldn't find " + key);
			state.PushNil();
		}

		return 1;
	}

	public virtual int NewIndex(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);

		if (state.Type(1) == LuaType.Vector)
		{
			throw new InvalidOperationException("Vector3 is read-only, create a new one with Vector3.New(...)");
		}

		Script script = LuauProvider.GetScriptInstance(state);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		string? key = state.ToAtomString(2, out int atom);

		if (key == null) { return 0; }

		// Set other script's globals
		if (targetObject is Script scriptref && targetObject.GetType() == script.GetType())
		{
			if (scriptref.Ran && scriptref.LuauMainThread != null && scriptref.ShouldContinue)
			{
				LuaState targetState = scriptref.LuauMainThread;

				state.PushValue(3);
				state.XMove(targetState, 1);
				targetState.SetSafeEnv(LuaState.LUA_GLOBALSINDEX, 0);
				targetState.SetGlobal(key);
				return 0;
			}
		}

		MemberEntry member = GetMember(key, atom, script.Compatibility);
		PropertyInfo? prop = member.Property;

		if (prop != null && member.CanWrite)
		{
			if (member.HasSetter)
			{
				// Read value
				object? propSetTo = LangProvider.LuaToObject(state, 3, getAsFunction: true);
				object? convertedValue = ScriptService.ConvertToPropertyType(propSetTo, prop.PropertyType);

				if (convertedValue == null && !IsNullableType(prop.PropertyType))
				{
					throw new Exception("member " + prop.Name + " cannot be assigned to nil.");
				}

				if (script.Compatibility)
				{
					prop.SetValue(targetObject, convertedValue);
				}
				else
				{
					PropertyAccessData accessData = member.Access;
					if (accessData.Setter == null)
					{
						throw new Exception("member " + prop.Name + " cannot be assigned to.");
					}
					accessData.Setter(targetObject!, convertedValue);
				}
				LuauProvider.WriteBackInline(state, 1, targetObject);
				return 0;
			}
			else
			{
				throw new Exception("member " + prop.Name + " cannot be assigned to.");
			}
		}
		else
		{
			if (!script.Compatibility)
			{
				throw new Exception("member " + key + " doesn't exist");
			}
			else
			{
				GD.PushWarning("member " + key + " doesn't exist");
			}
			return 0;
		}
	}

	public virtual int NameCall(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		Script script = LuauProvider.GetScriptInstance(state);

		string? key = state.GetNameCallAtom(out int atom);
		bool isFallback = string.IsNullOrEmpty(key);

		if (isFallback)
		{
			// Fallback for edge cases where __namecall is invoked manually
			key = state.ToAtomString(2, out atom);
		}

		if (key == null) { throw new InvalidOperationException($"namecall key is null"); }

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		MemberEntry member = GetMember(key, atom, script.Compatibility);
		ScriptService.MethodsCacheData methodInfos = member.Methods;

		if (methodInfos.Methods.Length == 0)
		{
			throw new InvalidOperationException($"target method doesn't exist ({key})");
		}

		// Process arguments
		int top = state.GetTop();
		int firstArgument = isFallback ? 3 : 2;

		int argumentCount = Math.Max(0, top - firstArgument + 1) + (methodInfos.SemiStatic ? 1 : 0);
		object?[] args = new object?[argumentCount];
		int argumentIndex = 0;

		// Push self if function is semi-static.
		if (methodInfos.SemiStatic)
		{
			args[argumentIndex++] = LangProvider.LuaToObject(state, 1, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction);
		}

		for (int i = firstArgument; i <= top; i++)
		{
			args[argumentIndex++] = LangProvider.LuaToObject(state, i, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction);
		}

		return ProcessMethod(state, script, methodInfos, member.Overloads, targetObject, key, args);
	}

	public int IndexWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return Index(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public int NewIndexWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return NewIndex(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public int NameCallWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return NameCall(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public virtual void RegisterMetamethods()
	{
		var grouped = ScriptService.GetMetamethods(TargetType)
			.GroupBy(x => x.Attribute.Metamethod);

		foreach (var group in grouped)
		{
			var attr = group.First().Attribute;
			var methods = group.Select(x => x.Method).ToArray();
			var stateInvoker = ScriptInterfaceInvokers.TryGetInvoker(methods[0]);
			List<OverloadEntry> overloads = [];

			HandlesLuaStateAttribute? handleLuaState = methods[0].GetCustomAttribute<HandlesLuaStateAttribute>();

			string indexer = attr.Metamethod switch
			{
				ScriptObjectMetamethod.Add => "__add",
				ScriptObjectMetamethod.Sub => "__sub",
				ScriptObjectMetamethod.Call => "__call",
				ScriptObjectMetamethod.Concat => "__concat",
				ScriptObjectMetamethod.Div => "__div",
				ScriptObjectMetamethod.Eq => "__eq",
				ScriptObjectMetamethod.Iter => "__iter",
				ScriptObjectMetamethod.Le => "__le",
				ScriptObjectMetamethod.Len => "__len",
				ScriptObjectMetamethod.Lt => "__lt",
				ScriptObjectMetamethod.Mod => "__mod",
				ScriptObjectMetamethod.Mul => "__mul",
				ScriptObjectMetamethod.Pow => "__pow",
				ScriptObjectMetamethod.ToString => "__tostring",
				ScriptObjectMetamethod.Unm => "__unm",
				ScriptObjectMetamethod.Index => "__index",
				ScriptObjectMetamethod.NewIndex => "__newindex",
				_ => ""
			};

			// Allow nil in argument on some metamethods
			// Some callers incorrectly pass nil to metamethods, unsupporting metamethods will have nil stripped from their argument list.
			bool allowNilInArg = attr.Metamethod is
				ScriptObjectMetamethod.Call or
				ScriptObjectMetamethod.Index or
				ScriptObjectMetamethod.NewIndex or
				ScriptObjectMetamethod.Iter;

			if (attr.Metamethod == ScriptObjectMetamethod.Index)
			{
				HasCustomIndex = true;
			}
			else if (attr.Metamethod == ScriptObjectMetamethod.NewIndex)
			{
				HasCustomNewIndex = true;
			}
			else if (attr.Metamethod == ScriptObjectMetamethod.ToString)
			{
				HasToString = true;
			}

			// Metamethod implementation
			int metamethodFunc(IntPtr L)
			{
				LuaState state = LuaState.FromIntPtr(L);

				object? targetObject = LangProvider.LuaToObject(state, 1, false);

				if (handleLuaState != null)
				{
					return (int)(stateInvoker != null ? stateInvoker(targetObject, [state]) : methods[0].Invoke(targetObject, [state]))!;
				}

				int additional = 0;

				if (attr.Metamethod == ScriptObjectMetamethod.Index || attr.Metamethod == ScriptObjectMetamethod.NewIndex)
				{
					additional = 1;
				}

				int argsCount = state.GetTop() - additional;

				object? val;

				// Ignore for equals in-case of deletion
				if (IsThisInvalid(targetObject) && attr.Metamethod != ScriptObjectMetamethod.Eq)
				{
					state.PushNil();
					return 1;
				}

				object?[] args = new object?[argsCount];
				int argIndex = 0;
				for (int i = 0; i < argsCount; i++)
				{
					int index = i + 1 + additional;
					var v = index == 1 && !attr.ConvertParamsToGD ? targetObject : LangProvider.LuaToObject(state, index, attr.ConvertParamsToGD);
					if (v != null || allowNilInArg)
						args[argIndex++] = v;
				}

				if (argIndex != args.Length)
					Array.Resize(ref args, argIndex);

				MethodInfo m = ResolveOverload(methods, overloads, args, out bool hasParams, out MethodInvocationData invocation) ?? throw new Exception("couldn't find metamethod with matching overload");

				try
				{
					val = invocation.Invoker is { } invoker ? invoker(targetObject, args) : m.Invoke(targetObject, args);

					// Handle __iter: push an iterator function, nil, nil
					if (attr.Metamethod is ScriptObjectMetamethod.Iter)
					{
						if (val is not IEnumerable enumerable)
						{
							state.PushNil();
							return 1;
						}

						IEnumerator enumerator = enumerable.GetEnumerator();

						int iteratorFunc(IntPtr iterL)
						{
							LuaState iterState = LuaState.FromIntPtr(iterL);

							if (!enumerator.MoveNext())
							{
								// Signal iteration end
								iterState.PushNil();
								return 1;
							}

							object current = enumerator.Current;

							// Unpack the tuple
							if (current is ITuple tuple && tuple.Length == 2)
							{
								LangProvider.PushValueToLua(iterState, tuple[0]);
								LangProvider.PushValueToLua(iterState, tuple[1]);
								return 2;
							}

							// Fallback: push the whole value
							LangProvider.PushValueToLua(iterState, current);
							return 1;
						}

						int safeIteratorFunc(IntPtr L)
						{
							Exception? caughtException;

							try
							{
								return iteratorFunc(L);
							}
							catch (Exception ex)
							{
								caughtException = ex;
							}

							if (caughtException != null)
							{
								LuaState state = LuaState.FromIntPtr(L);
								return state.Error(TargetType.Name + " metamethod error " + indexer + ": " + (caughtException.InnerException?.Message ?? caughtException.Message));
							}

							return 0;
						}

						// Push: iterator function, nil (state), nil (initial control value)
						state.PushCFunction(safeIteratorFunc, indexer + "_iter");
						state.PushNil();
						state.PushNil();
						return 3;
					}

					LangProvider.PushValueToLua(state, val);
					return 1;
				}
				catch (Exception ex)
				{
					PT.PrintErrV(ex.Message, " argn: ", args.Length);
					foreach (var item in args)
					{
						PT.PrintErrV(item, $" ({item?.GetType()})");
					}
					throw;
				}
			}

			int safeMetaMethodFunc(IntPtr L)
			{
				Exception? caughtException;

				try
				{
					return metamethodFunc(L);
				}
				catch (Exception ex)
				{
					caughtException = ex;
				}

				if (caughtException != null)
				{
					LuaState state = LuaState.FromIntPtr(L);
					return state.Error(TargetType.Name + " metamethod error " + indexer + ": " + (caughtException.InnerException?.Message ?? caughtException.Message));
				}

				return 0;
			}

			Lua.PushCFunction(safeMetaMethodFunc, indexer);

			Lua.SetField(-2, indexer);
		}

		if (TargetType == typeof(PTVector3))
		{
			Lua.SetSafeEnv(LuaState.LUA_GLOBALSINDEX, 1);
			Lua.Load("=Vector3", LuaState.Compile(VectorPrelude));
			Lua.PushCFunction(IndexWrapper, "__index");
			Lua.Call(1, 2);

			foreach (MethodInfo m in TargetType.GetMethods(BindingFlags.Public | BindingFlags.Static))
			{
				if (m.GetCustomAttribute<ScriptMethodAttribute>() is not { } attr)
					continue;

				string name = attr.MethodName ?? m.Name;
				Lua.GetField(-1, name);
				bool exists = !Lua.IsNil(-1);
				Lua.Pop(1);
				if (exists)
					continue;

				Lua.PushCFunction(NewMethodFunction(name, GetMember(name, -1, false), null, null), name);
				Lua.SetField(-2, name);
			}

			Lua.PushNil();
			while (Lua.Next(-2))
			{
				string name = Lua.ToString(-2)!;
				int funcRef = Lua.Ref();
				_staticFunctionRefs[(name, false)] = funcRef;
				_staticFunctionRefs[(name, true)] = funcRef;
			}
			Lua.Pop(1);

			Lua.SetField(-2, "__index");
			HasCustomIndex = true;
			HasCustomNameCall = true;
		}
	}

	public int ToString(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (targetObject != null)
		{
			state.PushString("<" + targetObject.GetType().Name + ">");
		}
		else
		{
			state.PushString("<null>");
		}

		return 1;
	}

	private static bool IsThisInvalid(object? targetObject)
	{
		if (targetObject is NetworkedObject netObj && netObj.IsDeleted)
		{
			return true;
		}
		return false;
	}

	private int ProcessMethod(LuaState state, Script script, ScriptService.MethodsCacheData methodInfos, List<OverloadEntry> overloads, object? targetObject, string key, object?[] args)
	{
		int argsCount = args.Length;

		MethodInfo? targetMethod = ResolveOverload(methodInfos.Methods, overloads, args, out bool hasParams, out MethodInvocationData invocationData);

		if (targetMethod == null)
		{
			if (!script.Compatibility)
			{
				throw new InvalidOperationException("couldn't find method with matching overload (" + key + ")");
			}
			else
			{
				PT.PrintErr(script.LuaPath, " couldn't find method with matching overload (" + key + ")");
			}
			return 0;
		}

		bool methodIsStatic = targetMethod.IsStatic;

		if (targetObject == null && !methodIsStatic)
		{
			throw new Exception("attempt to call non static method on a static object (" + key + ")");
		}

		if (invocationData.Permissions != ScriptPermissionFlags.None &&
			!script.PermissionFlags.HasFlag(invocationData.Permissions))
		{
			throw new UnauthorizedAccessException("script does not have permission to call the specified method (" + targetMethod.Name + ")");
		}

		if (invocationData.ObsoleteMessage != null)
		{
			PT.PrintWarn($"{targetMethod.Name} is obsolete. {invocationData.ObsoleteMessage}");
		}

		// Prepare args array formatted for params
		ParameterInfo[] parametersFinal = invocationData.Parameters;
		int callerParamIndex = invocationData.CallerParamIndex;

		object?[] invokeArgs = new object?[parametersFinal.Length];

		if (hasParams)
		{
			int fixedParamCount = parametersFinal.Length - 1;
			int paramsCount = Math.Max(0, argsCount - fixedParamCount);
			Type paramsElementType = parametersFinal[^1].ParameterType.GetElementType()!;

			// Convert fixed args
			for (int i = 0; i < fixedParamCount; i++)
				invokeArgs[i] = ScriptService.ConvertToPropertyType(args[i], parametersFinal[i].ParameterType);

			// Convert params args
			Array paramsArray = new object[paramsCount];
			for (int i = 0; i < paramsCount; i++)
				paramsArray.SetValue(ScriptService.ConvertToPropertyType(args[fixedParamCount + i], paramsElementType), i);

			invokeArgs[^1] = paramsArray;
		}
		else
		{
			// No params, just convert arguments
			int argIndex = 0;
			for (int i = 0; i < parametersFinal.Length; i++)
			{
				if (i == callerParamIndex)
					continue; // Skip the caller parameter

				if (argIndex < argsCount)
				{
					invokeArgs[i] = ScriptService.ConvertToPropertyType(args[argIndex], parametersFinal[i].ParameterType);
					argIndex++;
				}
				else
				{
					invokeArgs[i] = parametersFinal[i].HasDefaultValue ? parametersFinal[i].DefaultValue : null;
				}
			}
		}

		// Apply caller if ScriptingCallerAttribute is found
		if (callerParamIndex >= 0)
			invokeArgs[callerParamIndex] = script;

		if (invocationData.IsAsync)
		{
			TaskCompletionSource<int> tcs = new();
			LuauProvider.SetYieldTask(state, tcs.Task);
			AsyncExec(script, targetObject, state, targetMethod, invocationData.Invoker, invokeArgs, tcs);
			return state.Yield(1);
		}
		else
		{
			object? result = script.Compatibility
				? targetMethod.Invoke(targetObject, invokeArgs)
				: (invocationData.Invoker ?? throw new InvalidOperationException($"{targetMethod.Name} has no generated invoker"))(targetObject, invokeArgs);
			LangProvider.PushValueToLua(state, result);
			return 1;
		}
	}

	private static PropertyAccessData GetPropertyAccessData(PropertyInfo property)
	{
		if (_propertyAccessCache.TryGetValue(property, out PropertyAccessData cached))
			return cached;

		ScriptPropertyAttribute? scriptAttribute = property.GetCustomAttribute<ScriptPropertyAttribute>();
		Attributes.ObsoleteAttribute? obsoleteAttribute = property.GetCustomAttribute<Attributes.ObsoleteAttribute>();
		PropertyAccessData result = new(
			scriptAttribute?.Permissions ?? ScriptPermissionFlags.None,
			obsoleteAttribute?.Message,
			ScriptInterfaceInvokers.TryGetGetter(property),
			ScriptInterfaceInvokers.TryGetSetter(property));
		_propertyAccessCache[property] = result;
		return result;
	}

	private static MethodInvocationData GetMethodInvocationData(MethodInfo method)
	{
		if (_methodInvocationCache.TryGetValue(method, out MethodInvocationData cached))
			return cached;

		ParameterInfo[] parameters = method.GetParameters();
		ScriptMethodAttribute? scriptAttribute = method.GetCustomAttribute<ScriptMethodAttribute>();
		Attributes.ObsoleteAttribute? obsoleteAttribute = method.GetCustomAttribute<Attributes.ObsoleteAttribute>();
		MethodInvocationData result = new(
			parameters,
			Array.FindIndex(parameters, p => p.IsDefined(typeof(ScriptingCallerAttribute))),
			scriptAttribute?.Permissions ?? ScriptPermissionFlags.None,
			obsoleteAttribute?.Message,
			ScriptService.IsAsyncMethod(method),
			ScriptInterfaceInvokers.TryGetInvoker(method));
		_methodInvocationCache[method] = result;
		return result;
	}

	private static bool IsNullableType(Type type)
	{
		// Reference types are always nullable
		if (!type.IsValueType) return true;

		// Check for Nullable<T> (e.g. int?, float?)
		return Nullable.GetUnderlyingType(type) != null;
	}

	private LuaFunction NewMethodFunction(string key, MemberEntry member, object? targetObject, Script? script)
	{
		bool isStatic = targetObject == null;
		ScriptService.MethodsCacheData methodInfos = member.Methods;

		int methodFunc(IntPtr L)
		{
			LuaState state = LuaState.FromIntPtr(L);
			Script caller = isStatic ? LuauProvider.GetScriptInstance(state) : script!;

			int top = state.GetTop();
			if (methodInfos.Methods.Length == 0)
			{
				throw new InvalidOperationException("target method doesn't exist (" + key + ")");
			}

			object? firstArgument = null;
			bool skipFirstArgument = false;
			if (top > 0)
			{
				firstArgument = LangProvider.LuaToObject(state, 1, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction);
				skipFirstArgument = firstArgument == targetObject;
			}

			object?[] args = new object?[top - (skipFirstArgument ? 1 : 0)];
			int argumentIndex = 0;
			if (top > 0 && !skipFirstArgument)
				args[argumentIndex++] = firstArgument;

			for (int i = 2; i <= top; i++)
			{
				args[argumentIndex++] = LangProvider.LuaToObject(state, i, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction);
			}

			return ProcessMethod(state, caller, methodInfos, member.Overloads, targetObject, key, args);
		}

		int safeMethodFunc(IntPtr L)
		{
			Exception? caughtException;

			try
			{
				return methodFunc(L);
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

		return safeMethodFunc;
	}

	private bool TryPushStaticFunction(LuaState state, string key, bool compatibility)
	{
		if (!_staticFunctionRefs.TryGetValue((key, compatibility), out int funcRef))
			return false;

		state.GetRef(funcRef);
		return true;
	}

	private int PushStaticFunction(LuaState state, string key, bool compatibility, LuaFunction func)
	{
		LangProvider.GlobalLuaState.PushCFunction(func, key);
		int funcRef = LangProvider.GlobalLuaState.Ref();
		_staticFunctionRefs[(key, compatibility)] = funcRef;
		state.GetRef(funcRef);
		return 1;
	}

	private MemberEntry GetMember(string key, int atom, bool compatibility)
	{
		ref MemberEntry?[] members = ref compatibility ? ref _legacyMembers : ref _members;
		if ((uint)atom < (uint)members.Length && members[atom] is { } cached)
			return cached;

		if (atom < 0 && _namedMembers.TryGetValue((key, compatibility), out MemberEntry? named))
			return named;

		PropertyInfo? prop = ScriptService.GetScriptPropertyOfName(TargetType, key, compatibility);
		ScriptService.MethodsCacheData methods = ScriptService.ResolveMethods(TargetType, key, compatibility);
		MemberEntry member = new()
		{
			Property = prop,
			Access = prop != null ? GetPropertyAccessData(prop) : default,
			CanWrite = prop != null && prop.CanWrite,
			HasSetter = prop?.GetSetMethod() != null,
			Methods = methods,
			HandlesLuaState = methods.Methods.Length > 0 && methods.Methods[0].IsDefined(typeof(HandlesLuaStateAttribute)),
		};

		if (atom >= 0)
		{
			if (atom >= members.Length)
				Array.Resize(ref members, Math.Max(atom + 1, members.Length * 2));
			members[atom] = member;
		}
		else if (prop != null || methods.Methods.Length > 0)
		{
			_namedMembers[(key, compatibility)] = member;
		}

		return member;
	}

	private static MethodInfo? ResolveOverload(MethodInfo[] methods, List<OverloadEntry> entries, object?[] args, out bool hasParams, out MethodInvocationData invocation)
	{
		hasParams = false;
		invocation = default;
		if (methods.Length == 0) return null;

		foreach (OverloadEntry entry in entries)
		{
			if (entry.Matches(args))
			{
				hasParams = entry.HasParams;
				invocation = entry.Invocation;
				return entry.Method;
			}
		}

		MethodInfo? result = ResolveOverloadUncached(methods, args, out hasParams);
		if (result != null)
			invocation = GetMethodInvocationData(result);
		if (entries.Count < 16)
			entries.Add(new(Array.ConvertAll(args, static a => a?.GetType()), result, hasParams, invocation));
		return result;
	}

	private static MethodInfo? ResolveOverloadUncached(MethodInfo[] methods, object?[] args, out bool hasParams)
	{
		hasParams = false;

		foreach (var m in methods)
		{
			ParameterInfo[] parameters = m.GetParameters();
			ParameterInfo[] eligible = [.. parameters.Where(p => !p.IsDefined(typeof(ScriptingCallerAttribute)))];

			bool isParams = eligible.Length > 0 && Attribute.IsDefined(eligible[^1], typeof(ParamArrayAttribute));
			int fixedCount = isParams ? eligible.Length - 1 : eligible.Length;

			// Arity checks
			if (!isParams && args.Length > eligible.Length) continue;
			if (isParams && args.Length < fixedCount) continue;

			// Type compatibility
			bool compatible = true;

			// All fixed params beyond args must have defaults
			for (int i = args.Length; i < fixedCount; i++)
			{
				if (!eligible[i].HasDefaultValue)
				{
					// Allow missing args if the param accepts null
					Type pt = eligible[i].ParameterType;
					bool acceptsNull = !pt.IsValueType || Nullable.GetUnderlyingType(pt) != null;
					if (!acceptsNull) { compatible = false; break; }
				}
			}

			for (int i = 0; i < args.Length; i++)
			{
				Type paramType = (isParams && i >= fixedCount)
					? eligible[^1].ParameterType.GetElementType()!
					: eligible[i].ParameterType;

				object? arg = args[i];
				if (arg is null)
				{
					if (!paramType.IsClass && Nullable.GetUnderlyingType(paramType) == null)
					{ compatible = false; break; }
				}
				else
				{
					if (!paramType.IsAssignableFrom(arg.GetType()) &&
						!ScriptService.IsObjectConvertible(arg, paramType))
					{ compatible = false; break; }
				}
			}

			if (!compatible) continue;

			hasParams = isParams;
			return m;
		}

		return null;
	}

	private sealed class MemberEntry
	{
		public PropertyInfo? Property;
		public PropertyAccessData Access;
		public bool CanWrite;
		public bool HasSetter;
		public ScriptService.MethodsCacheData Methods;
		public bool HandlesLuaState;
		public readonly List<OverloadEntry> Overloads = [];
	}

	private readonly record struct OverloadEntry(Type?[] ArgTypes, MethodInfo? Method, bool HasParams, MethodInvocationData Invocation)
	{
		public bool Matches(object?[] args)
		{
			if (ArgTypes.Length != args.Length) return false;

			for (int i = 0; i < args.Length; i++)
				if (ArgTypes[i] != args[i]?.GetType()) return false;

			return true;
		}
	}

	private readonly record struct PropertyAccessData(
		ScriptPermissionFlags Permissions,
		string? ObsoleteMessage,
		Func<object, object?>? Getter,
		Action<object, object?>? Setter);

	private readonly record struct MethodInvocationData(
		ParameterInfo[] Parameters,
		int CallerParamIndex,
		ScriptPermissionFlags Permissions,
		string? ObsoleteMessage,
		bool IsAsync,
		Func<object?, object?[], object?>? Invoker);
}
