// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Polytoria.Scripting.Luau;

internal static partial class ScriptInterfaceInvokers
{
	internal const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	private readonly record struct PropertyKey(Type DeclaringType, string Name);
	private readonly record struct MethodKey(Type DeclaringType, string Name, string ParameterSignature);

	private static PropertyKey KeyOf(PropertyInfo property) => new(property.DeclaringType!, property.Name);
	private static MethodKey KeyOf(MethodInfo method) => new(method.DeclaringType!, method.Name, ParameterSignature(method));

	private static string ParameterSignature(MethodInfo method)
	{
		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length == 0) return string.Empty;
		return string.Join('|', Array.ConvertAll(parameters, static p => p.ParameterType.AssemblyQualifiedName));
	}

	private static readonly Dictionary<PropertyKey, Func<object, object?>> _getters = [];
	private static readonly Dictionary<PropertyKey, Action<object, object?>> _setters = [];
	private static readonly Dictionary<MethodKey, Func<object?, object?[], object?>> _invokers = [];

	private static void RegisterProperty(PropertyInfo property, Func<object, object?> getter, Action<object, object?>? setter)
	{
		_getters.Add(KeyOf(property), getter);

		if (setter != null)
			_setters.Add(KeyOf(property), setter);
	}

	private static void RegisterMethod(MethodInfo method, Func<object?, object?[], object?> invoker)
	{
		_invokers.Add(KeyOf(method), invoker);
	}

	public static Func<object, object?>? TryGetGetter(PropertyInfo property)
	{
		if (_getters.TryGetValue(KeyOf(property), out var getter))
			return getter;

		for (Type? baseType = property.DeclaringType!.BaseType; baseType != null; baseType = baseType.BaseType)
		{
			if (_getters.TryGetValue(new PropertyKey(baseType, property.Name), out getter))
				return getter;
		}

		return null;
	}

	public static Action<object, object?>? TryGetSetter(PropertyInfo property)
	{
		if (_setters.TryGetValue(KeyOf(property), out var setter))
			return setter;

		for (Type? baseType = property.DeclaringType!.BaseType; baseType != null; baseType = baseType.BaseType)
		{
			if (_setters.TryGetValue(new PropertyKey(baseType, property.Name), out setter))
				return setter;
		}

		return null;
	}

	public static Func<object?, object?[], object?>? TryGetInvoker(MethodInfo method)
	{
		MethodKey key = KeyOf(method);
		if (_invokers.TryGetValue(key, out var invoker))
			return invoker;

		for (Type? baseType = method.DeclaringType!.BaseType; baseType != null; baseType = baseType.BaseType)
		{
			if (_invokers.TryGetValue(new MethodKey(baseType, key.Name, key.ParameterSignature), out invoker))
				return invoker;
		}

		return null;
	}
}
