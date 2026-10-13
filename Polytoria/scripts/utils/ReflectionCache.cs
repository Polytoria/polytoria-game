using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Polytoria.Utils;

public static class ReflectionCache
{
	private static readonly Dictionary<MemberInfo, Dictionary<Type, bool>> _isDefinedCache = new();
	private static readonly Dictionary<MemberInfo, Dictionary<Type, Attribute?>> _customAttributeCache = new();
	private static readonly ConcurrentDictionary<(MemberInfo, Type, bool), bool> _isDefinedInheritCache = new();

	public static bool IsDefinedCached(this MemberInfo mi, Type attributeType)
	{
		if (!_isDefinedCache.TryGetValue(mi, out var inner))
		{
			inner = [];
			_isDefinedCache[mi] = inner;
		}

		if (inner.TryGetValue(attributeType, out var cachedResult))
			return cachedResult;

		var result = mi.IsDefined(attributeType);
		inner[attributeType] = result;

		return result;
	}
	public static bool IsDefinedCached(this MemberInfo mi, Type attributeType, bool inherit)
	{
		return _isDefinedInheritCache.GetOrAdd((mi, attributeType, inherit), static key => key.Item1.IsDefined(key.Item2, key.Item3));
	}

	public static T? GetCustomAttributeCached<T>(this MemberInfo mi) where T : Attribute
	{
		var attributeType = typeof(T);

		if (!_customAttributeCache.TryGetValue(mi, out var inner))
		{
			inner = [];
			_customAttributeCache[mi] = inner;
		}

		if (inner.TryGetValue(attributeType, out var cachedAttr))
			return (T?)cachedAttr;

		var attr = mi.GetCustomAttribute<T>();
		inner[attributeType] = attr;

		return attr;
	}
}
