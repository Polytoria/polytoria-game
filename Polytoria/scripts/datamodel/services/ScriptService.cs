// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Datamodel.Creator;
#endif
using Polytoria.Datamodel.Data;
using Polytoria.Scripting;
using Polytoria.Scripting.Datatypes;
using Polytoria.Scripting.Luau;
using Polytoria.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("ScriptService")]
public sealed partial class ScriptService : Instance
{
	private const DynamicallyAccessedMemberTypes DynamicallyAccessedTypes = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods;

	private static readonly Dictionary<CacheKey, MethodsCacheData> _methodsCache = [];
	private static readonly Dictionary<CacheKey, MethodInfo?> _methodCache = [];
	private static readonly Dictionary<CacheKey, PropertyInfo?> _propertyCache = [];
	private static readonly Dictionary<Type, (MethodInfo, ScriptMetamethodAttribute)[]> _metaMethodCache = [];

	// Dictionary of all proxies
	public static readonly Dictionary<Type, Type> ProxyMap = new()
	{
		{ typeof(Vector3), typeof(PTVector3) },
		{ typeof(Vector2), typeof(PTVector2) },
		{ typeof(Color), typeof(PTColor) },
		{ typeof(Quaternion), typeof(PTQuaternion) },
		{ typeof(Aabb), typeof(PTBounds) },
	};

	// Dictionary of all data type exposed to scripting
	public static readonly Dictionary<string, Type> GlobalDataMap = new()
	{
		{ "Vector3", typeof(PTVector3) },
		{ "Vector2", typeof(PTVector2) },
		{ "Quaternion", typeof(PTQuaternion) },
		{ "Color", typeof(PTColor) },
		{ "NetMessage", typeof(NetMessage) },
		{ "HttpRequestData", typeof(HttpRequestData) },
		{ "HttpResponseData", typeof(HttpResponseData) },
		{ "NewServerRequestData", typeof(NewServerRequestData) },
		{ "InputButton", typeof(InputButton) },
		{ "ColorSeries", typeof(ColorSeries) },
		{ "NumberRange", typeof(NumberRange) },
	};

	// Dictionary of all enum exposed to scripting (populated by source generator)
	public static readonly Dictionary<string, Type> EnumMap = new(ScriptEnumMapInitializer.EnumMap);

	private readonly Dictionary<ScriptLanguagesEnum, IScriptLanguageProvider> _languageProviders = [];

	public LogDispatcher Logger { get; private set; } = null!;

	public ScriptService()
	{
		if (Globals.UseNodes)
		{
			// Only allow node creation here, scripting will be disabled in non node env (eg. unit tests)
			RegisterLanguageProvider(ScriptLanguagesEnum.Luau, new LuauProvider());
		}
	}

	public override void Init()
	{
		Logger = new()
		{
			Name = "LogDispatch",
			Root = Root
		};
		Logger.InitEntry();
		Logger.SetNetworkParent(this);
		base.Init();
	}

	public override void PreDelete()
	{
		foreach (var provider in _languageProviders.Values)
		{
			provider.Dispose();
		}
		base.PreDelete();
	}

	public void Run(Script script)
	{
		PT.Print("Running script: ", script.LuaPath);

		if (!_languageProviders.TryGetValue(script.ChosenLanguage, out var provider))
		{
			throw new Exception(script.ChosenLanguage + " is not provided");
		}

		script.LanguageProvider = provider;
		provider.Run(script);
	}

	public void CompileScript(Script script)
	{
		if (!_languageProviders.TryGetValue(script.ChosenLanguage, out var provider))
		{
			throw new Exception(script.ChosenLanguage + " is not provided");
		}

		if (string.IsNullOrWhiteSpace(script.Source)) return;

		script.Bytecode = provider.CompileSource(script.Source);
	}

	public void Close(Script script)
	{
		PT.Print("Closing script: ", script.LuaPath);

		script.LanguageProvider.Close(script);
	}

	private void RegisterLanguageProvider(ScriptLanguagesEnum lang, IScriptLanguageProvider provider)
	{
		_languageProviders.Add(lang, provider);
	}

	public static Dictionary<string, IScriptObject?> GetStaticObjects(World root, Script? script = null)
	{
		Dictionary<string, IScriptObject?> dir = new()
		{
			{ "Environment", root.Environment },
			{ "Lighting", root.Lighting },
			{ "Players", root.Players },
			{ "ScriptService", root.ScriptService },
			{ "Hidden", root.Hidden },
			{ "ServerHidden", root.ServerHidden },
			{ "PlayerDefaults", root.PlayerDefaults },
			{ "PlayerGUI", root.PlayerGUI },
			{ "Chat", root.Chat },
			{ "Filter", root.Filter },
			{ "Camera", root.Environment.CurrentCamera },
			{ "Assets", root.Assets },
			{ "Input", root.Input },
			{ "Achievements", root.Achievements },
			{ "Tween", root.Tween },
			{ "CoreUI", root.CoreUI },
			{ "Stats", root.Stats },
			{ "Teams", root.Teams },
			{ "Datastore", root.Datastore },
			{ "Http", root.Http },
			{ "Insert", root.Insert },
			{ "Purchases", root.Purchases },
			{ "Capture", root.Capture },
			{ "Presence", root.Presence },
			{ "Preferences", root.Preferences },
			{ "Worlds", root.Worlds },
		};

		if (script != null)
		{
#if CREATOR
			if (script.PermissionFlags.HasFlag(ScriptPermissionFlags.CreatorAccess))
			{
				dir.Add("Creator", CreatorService.Singleton);
			}
#endif
			if (script.PermissionFlags.HasFlag(ScriptPermissionFlags.IORead | ScriptPermissionFlags.IOWrite))
			{
				dir.Add("IO", root.IO);
			}
#if CREATOR
			if (script.PermissionFlags.HasFlag(ScriptPermissionFlags.ContextAccess))
			{
				dir.Add("Context", root.CreatorContext);
				dir.Add("Addons", root.CreatorContext.Addons);
				dir.Add("Selections", root.CreatorContext.Selections);
				dir.Add("History", root.CreatorContext.History);
				dir.Add("CreatorGUI", root.CreatorContext.GUIOverlay);
				dir["Camera"] = root.CreatorContext.Freelook;
			}
#endif
		}

		return dir;
	}


	internal static PropertyInfo? GetScriptPropertyOfName(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type,
		string name,
		bool compat = false)
	{
		CacheKey cacheKey = new() { Type = type, Key = name, IsCompatibility = compat };

		// Try to get from cache first
		if (_propertyCache.TryGetValue(cacheKey, out PropertyInfo? cached))
			return cached;

		PropertyInfo? result = null;

		PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);

		if (!compat)
		{
			result = props.FirstOrDefault(p =>
				p.IsDefined(typeof(ScriptPropertyAttribute), false) &&
				p.Name == name);
		}
		else
		{
			// try legacy properties first
			// and then case-insensitive ScriptPropertyAttribute
			result = props.FirstOrDefault(p =>
			{
				ScriptLegacyPropertyAttribute? legacyAttr = p.GetCustomAttribute<ScriptLegacyPropertyAttribute>();
				return legacyAttr != null &&
					   legacyAttr.PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase);
			}) ?? props.FirstOrDefault(p =>
					p.IsDefined(typeof(ScriptPropertyAttribute), false) &&
					p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		}

		// Cache result
		_propertyCache.TryAdd(cacheKey, result);

		return result;
	}

	public static void FreePTCallback(PTCallback action)
	{
		action.LangProvider?.FreePTCallback(action);
	}

	internal static MethodInfo? ResolveMethod(
		bool compatibility,
		string key,
		[DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type type)
	{

		CacheKey cacheKey = new() { Type = type, Key = key, IsCompatibility = compatibility };

		// Try to get from cache first
		if (_methodCache.TryGetValue(cacheKey, out MethodInfo? cachedMethod))
			return cachedMethod;

		MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
		MethodInfo? method = null;

		if (compatibility)
		{
			// Find legacy method first
			method = methods.FirstOrDefault(p =>
				p.IsDefined(typeof(ScriptLegacyMethodAttribute)) &&
				string.Equals(
					p.GetCustomAttribute<ScriptLegacyMethodAttribute>()?.MethodName,
					key,
					StringComparison.CurrentCultureIgnoreCase));

			// If not found, fallback to ScriptMethodAttribute

			if (method == null)
			{
				method = methods.FirstOrDefault(p =>
					p.IsDefined(typeof(ScriptMethodAttribute)) &&
					(p.Name.Equals(key, StringComparison.CurrentCultureIgnoreCase) ||
					 string.Equals(
						 p.GetCustomAttribute<ScriptMethodAttribute>()?.MethodName,
						 key,
						 StringComparison.CurrentCultureIgnoreCase)));
			}
		}
		else
		{
			// No compat, lookup normal method attribute
			method = methods.FirstOrDefault(p =>
				p.IsDefined(typeof(ScriptMethodAttribute)) &&
				(p.Name == key ||
				 p.GetCustomAttribute<ScriptMethodAttribute>()?.MethodName == key));
		}

		// Cache the result
		_methodCache.TryAdd(cacheKey, method);

		return method;
	}

	internal static (MethodInfo Method, ScriptMetamethodAttribute Attribute)[] GetMetamethods(
			[DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type type)
	{
		// Try to get from cache first
		if (_metaMethodCache.TryGetValue(type, out (MethodInfo, ScriptMetamethodAttribute)[]? cached))
			return cached;

		(MethodInfo, ScriptMetamethodAttribute)[] metamethods = [.. type
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			.Select(m => (Method: m, Attribute: m.GetCustomAttribute<ScriptMetamethodAttribute>(true)!))
			.Where(t => t.Attribute != null)];

		_metaMethodCache.TryAdd(type, metamethods);

		return metamethods;
	}


	internal static bool IsObjectConvertible(object arg, Type targetType)
	{
		if (targetType == typeof(object)) return true;

		Type argType = arg.GetType();
		if (argType == targetType) return true;
		if (targetType.IsAssignableFrom(argType)) return true;

		// unwrap Nullable and check the inner type
		Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		// double to string
		if (targetType == typeof(string) && arg is IConvertible)
			return true;

		// Enum, any integer-ish value works
		if (underlying.IsEnum)
			return arg is int or long or short or byte or double;

		// Numeric widening from double
		if (arg is double)
			return underlying == typeof(float)
				|| underlying == typeof(int)
				|| underlying == typeof(long)
				|| underlying == typeof(short);

		// Array target, check element type compatibility
		if (targetType.IsArray && arg is object[] objArr)
		{
			Type? elemType = targetType.GetElementType();
			if (elemType == null) return false;
			foreach (object? elem in objArr)
				if (elem != null && !elemType.IsAssignableFrom(elem.GetType()))
					return false;
			return true;
		}

		// Empty array to dictionary
		if (arg is Array { Length: 0 } && typeof(IDictionary).IsAssignableFrom(targetType))
			return true;

		// Empty dictionary to array
		if (arg is IDictionary { Count: 0 } && targetType.IsArray)
			return true;

		// Dictionary to dictionary
		if (arg is IDictionary srcDict && typeof(IDictionary).IsAssignableFrom(targetType))
		{
			if (!targetType.IsGenericType) return true;
			Type[] ga = targetType.GetGenericArguments();
			if (ga.Length != 2) return true;
			Type keyType = ga[0], valType = ga[1];
			foreach (DictionaryEntry entry in srcDict)
			{
				if (entry.Key != null && !keyType.IsAssignableFrom(entry.Key.GetType()))
					return false;
				if (entry.Value != null && !valType.IsAssignableFrom(entry.Value.GetType()))
					return false;
			}
			return true;
		}

		// IConvertible fallback
		if (arg is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
			return true;

		// string to double
		if (arg is string s && (underlying == typeof(int) || underlying == typeof(long)
			|| underlying == typeof(short) || underlying == typeof(float) || underlying == typeof(double)))
			return double.TryParse(s, out _);

		return false;
	}

	internal static object? ConvertToPropertyType(object? value, Type targetType)
	{
		if (value == null)
			return null;

		// If types already match, return as-is
		if (targetType.IsInstanceOfType(value))
			return value;

		// If targetType is a dictionary and value is an empty array, create an empty dictionary instance
		if (value is Array arr && arr.Length == 0)
		{
			if (typeof(IDictionary).IsAssignableFrom(targetType))
			{
				return CreateInstanceForType(targetType);
			}

			if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
			{
				return CreateInstanceForType(targetType);
			}
		}

		// If targetType is an array and value is an empty dictionary, create an empty array instance
		if (value is IDictionary emptyDict && emptyDict.Count == 0 && targetType.IsArray)
		{
			Type? targetElementType = targetType.GetElementType();
			if (targetElementType != null)
				return ConvertListToArray([], targetElementType);
		}

		// Both Dictionary, try convert
		if (value is IDictionary sourceDict && typeof(IDictionary).IsAssignableFrom(targetType))
		{
			Type? targetKeyType = null;
			Type? targetValueType = null;

			if (targetType.IsGenericType)
			{
				Type[] genericArgs = targetType.GetGenericArguments();
				if (genericArgs.Length == 2)
				{
					targetKeyType = genericArgs[0];
					targetValueType = genericArgs[1];
				}
			}

			if (CreateInstanceForType(targetType) is not IDictionary targetDict)
				return null;

			// Convert and copy each key-value pair
			foreach (DictionaryEntry entry in sourceDict)
			{
				object? convertedKey = entry.Key;
				object? convertedValue = entry.Value;

				// Convert key if target key type is known
				if (targetKeyType != null && entry.Key != null)
				{
					convertedKey = ConvertToPropertyType(entry.Key, targetKeyType);
				}

				// Convert value if target value type is known
				if (targetValueType != null && entry.Value != null)
				{
					convertedValue = ConvertToPropertyType(entry.Value, targetValueType);
				}

				if (convertedKey != null)
				{
					targetDict[convertedKey] = convertedValue;
				}
			}

			return targetDict;
		}

		// Handle object[] with uniform types
		if (value is object[] objectArray && objectArray.Length > 0 && targetType.IsArray)
		{
			Type? targetElementType = targetType.GetElementType();
			if (targetElementType == null)
				return null;

			// Check if all elements are assignable to target element type
			bool allCompatible = true;

			for (int i = 0; i < objectArray.Length; i++)
			{
				if (objectArray[i] != null && !targetElementType.IsAssignableFrom(objectArray[i].GetType()))
				{
					allCompatible = false;
					break;
				}
			}

			// If all elements are compatible, create a typed array
			if (allCompatible)
			{
				List<object> convertedList = [];
				for (int i = 0; i < objectArray.Length; i++)
				{
					object? convertedElement = ConvertToPropertyType(objectArray[i], targetElementType);
					if (convertedElement != null)
					{
						convertedList.Add(convertedElement);
					}
				}
				return ConvertListToArray(convertedList, targetElementType);
			}
		}

		// Handle nullable types
		Type underlayingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (targetType.IsEnum)
		{
			return Enum.ToObject(targetType, Convert.ToInt32(value));
		}

		// Handle common numeric conversions
		if (value is double doubleValue)
		{
			if (underlayingType == typeof(float))
				return (float)doubleValue;
			if (underlayingType == typeof(int))
				return (int)doubleValue;
			if (underlayingType == typeof(long))
				return (long)doubleValue;
			if (underlayingType == typeof(short))
				return (short)doubleValue;
		}

		try
		{
			return Convert.ChangeType(value, underlayingType);
		}
		catch
		{
			if (Globals.IsBetaBuild)
			{
				throw new Exception("Property type is invalid, tried to convert " + value.GetType().Name + " to " + targetType.Name);
			}
			else
			{
				throw new Exception("Property type is invalid");
			}
		}
	}

	internal static bool IsAsyncMethod(MethodInfo method)
	{
		if (method.ReturnType == typeof(Task)) return true;
		Type attType = typeof(AsyncStateMachineAttribute);

		// Obtain the custom attribute for the method. 
		// The value returned contains the StateMachineType property. 
		// Null is returned if the attribute isn't present for the method. 
		AsyncStateMachineAttribute? attrib = (AsyncStateMachineAttribute?)method.GetCustomAttribute(attType);

		return (attrib != null);
	}

	internal static MethodsCacheData ResolveMethods([DynamicallyAccessedMembers(DynamicallyAccessedTypes)] Type type, string key, bool compatibility)
	{
		CacheKey cacheKey = new() { Type = type, Key = key, IsCompatibility = compatibility };

		if (_methodsCache.TryGetValue(cacheKey, out MethodsCacheData cached))
		{
			return cached;
		}

		// Get all methods once
		MethodInfo[] methods = type.GetMethods(
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy
		);

		IEnumerable<MethodInfo> methodInfos = compatibility
			? methods.Where(m =>
				m.IsDefined(typeof(ScriptLegacyMethodAttribute)) &&
				m.GetCustomAttribute<ScriptLegacyMethodAttribute>()?.MethodName?.Equals(key, StringComparison.CurrentCultureIgnoreCase) == true)
			: methods.Where(m =>
				m.Name.Equals(key) ||
				m.GetCustomAttribute<ScriptMethodAttribute>()?.MethodName == key);

		if (compatibility && !methodInfos.Any())
		{
			methodInfos = methods.Where(m =>
				m.IsDefined(typeof(ScriptMethodAttribute)) &&
				(m.Name.Equals(key, StringComparison.CurrentCultureIgnoreCase) ||
				 m.GetCustomAttribute<ScriptMethodAttribute>()?.MethodName?.Equals(key, StringComparison.CurrentCultureIgnoreCase) == true));
		}

		bool convertParamsToGD = methodInfos.Any(m =>
			m.GetCustomAttributes<ScriptMethodAttribute>().Any(attr => attr.ConvertParamsToGD == true) ||
			m.GetCustomAttributes<ScriptLegacyMethodAttribute>().Any(attr => attr.ConvertParamsToGD == true));

		bool getParamsAsFunction = methodInfos.Any(m =>
			m.GetCustomAttributes<ScriptMethodAttribute>().Any(attr => attr.GetParamsAsFunction == true));

		bool semiStatic = methodInfos.Any(m =>
			m.GetCustomAttributes<ScriptMethodAttribute>().Any(attr => attr.SemiStatic == true));

		MethodsCacheData cacheData = new()
		{
			Methods = [.. methodInfos],
			ConvertParamsToGD = convertParamsToGD,
			GetParamsAsFunction = getParamsAsFunction,
			SemiStatic = semiStatic,
		};

		_methodsCache[cacheKey] = cacheData;

		return cacheData;
	}

	// --------------- HANDLE INSTANCE FOR TYPES --------------- 
	internal static object CreateInstanceForType(Type targetType)
	{
		// Instance types
		if (targetType == typeof(Instance))
			return new Instance();
		if (targetType == typeof(Physical))
			return new Physical();
		if (targetType == typeof(Dynamic))
			return new Dynamic();
		if (targetType == typeof(Environment.RayResult))
			return new Environment.RayResult();

		// Data types
		if (targetType == typeof(HttpResponseData))
			return new HttpResponseData();

		// Primitive types
		if (targetType == typeof(int))
			return 0;
		if (targetType == typeof(string))
			return string.Empty;

		// Dictionary types
		else if (targetType == typeof(Dictionary<string, int>))
			return new Dictionary<string, int>();
		else if (targetType == typeof(Dictionary<string, string>))
			return new Dictionary<string, string>();
		else if (targetType == typeof(Dictionary<string, object>))
			return new Dictionary<string, object>();
		else if (targetType == typeof(Dictionary<object, object>))
			return new Dictionary<object, object>();
		else if (targetType == typeof(Dictionary<int, string>))
			return new Dictionary<int, string>();

		throw new NotSupportedException($"INTERNAL BUG: CreateInstanceForType: Type {targetType} is not supported in AOT");
	}

	internal static object ConvertListToArray(List<object> list, Type elementType)
	{
		// Handle specific types explicitly for AOT compatibility
		if (elementType == typeof(int))
		{
			return list.Cast<int>().ToArray();
		}
		else if (elementType == typeof(string))
		{
			return list.Cast<string>().ToArray();
		}
		else if (elementType == typeof(double))
		{
			return list.Cast<double>().ToArray();
		}
		else if (elementType == typeof(float))
		{
			return list.Cast<float>().ToArray();
		}
		else if (elementType == typeof(bool))
		{
			return list.Cast<bool>().ToArray();
		}
		else if (elementType == typeof(long))
		{
			return list.Cast<long>().ToArray();
		}
		else if (elementType == typeof(object))
		{
			return list.ToArray();
		}

		// Instance types
		else if (elementType == typeof(NetworkedObject))
		{
			return list.Cast<NetworkedObject>().ToArray();
		}
		else if (elementType == typeof(Instance))
		{
			return list.Cast<Instance>().ToArray();
		}
		else if (elementType == typeof(Physical))
		{
			return list.Cast<Physical>().ToArray();
		}
		else if (elementType == typeof(Dynamic))
		{
			return list.Cast<Dynamic>().ToArray();
		}
		else if (elementType == typeof(Player))
		{
			return list.Cast<Player>().ToArray();
		}
#if CREATOR
		else if (elementType == typeof(CreatorAddons.AddonPermissionEnum))
		{
			return list.Cast<CreatorAddons.AddonPermissionEnum>().ToArray();
		}
#endif

		throw new NotSupportedException($"INTERNAL BUG: ConvertListToArray: Array element type {elementType} is not supported in AOT");
	}

	public struct MethodsCacheData
	{
		public MethodInfo[] Methods;
		public bool ConvertParamsToGD;
		public bool GetParamsAsFunction;
		public bool SemiStatic;
	}

	private struct CacheKey : IEquatable<CacheKey>
	{
		public Type Type;
		public string Key;
		public bool IsCompatibility;

		public readonly bool Equals(CacheKey other)
		{
			return Type == other.Type &&
				   Key.Equals(other.Key, StringComparison.CurrentCultureIgnoreCase) &&
				   IsCompatibility == other.IsCompatibility;
		}

		public override readonly bool Equals(object? obj)
		{
			return obj is CacheKey other && Equals(other);
		}

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(
				Type,
				Key,
				IsCompatibility
			);
		}
	}
}
