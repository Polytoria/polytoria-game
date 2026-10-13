// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Polytoria.Networking.Synchronizers;

public sealed class PropSyncProp
{
	public required string Name { get; init; }
	public required bool HasSyncVar { get; init; }
	public required bool AllowAuthorWrite { get; init; }
	public required bool ServerOnly { get; init; }
	public required bool Unreliable { get; init; }
	public required bool IsObjectRef { get; init; }
	public required Func<NetworkedObject, object?> GetValue { get; init; }
	public required Action<NetworkedObject, object?> SetValue { get; init; }
	public required Func<object?, byte[]> Serialize { get; init; }
	public required Func<byte[], object?> Deserialize { get; init; }
}

internal static partial class PropSyncRegistry
{
	private static readonly Dictionary<Type, PropSyncProp[]> _declaredProps = [];
	private static readonly ConcurrentDictionary<Type, Dictionary<string, PropSyncProp>> _resolvedProps = [];

	static PropSyncRegistry()
	{
		RegisterGenerated();
	}

	static partial void RegisterGenerated();

	private static void Register(Type type, PropSyncProp[] props)
	{
		_declaredProps[type] = props;
	}

	private static Dictionary<string, PropSyncProp> Resolve(Type type)
	{
		Dictionary<string, PropSyncProp> map = [];

		for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
		{
			if (_declaredProps.TryGetValue(t, out PropSyncProp[]? props))
			{
				foreach (PropSyncProp prop in props)
				{
					map.TryAdd(prop.Name, prop);
				}
			}
		}

		return map;
	}

	internal static PropSyncProp? GetProp(Type type, string name)
	{
		return _resolvedProps.GetOrAdd(type, Resolve).GetValueOrDefault(name);
	}

	internal static Dictionary<string, PropSyncProp> GetProps(Type type)
	{
		return _resolvedProps.GetOrAdd(type, Resolve);
	}

	internal static object? DeserializeEnum<T>(byte[] raw) where T : struct, Enum
	{
		try
		{
			int enumValue = SerializeUtils.Deserialize<int>(raw);

			if (!Enum.IsDefined(typeof(T), enumValue))
			{
				PT.PrintErr("Enum not defined: ", typeof(T).Name, ": ", enumValue);
				return null;
			}

			return (T)Enum.ToObject(typeof(T), enumValue);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			return null;
		}
	}
}
