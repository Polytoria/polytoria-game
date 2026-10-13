// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Polytoria.Formats;

public sealed class DatamodelMetaProp
{
	public required string Name { get; init; }
	public required PropertyInfo Property { get; init; }
	public required Type PropertyType { get; init; }
	public required bool IsEditable { get; init; }
	public required bool HasSaveInclude { get; init; }
	public required bool HasCloneInclude { get; init; }
	public required bool HasCloneIgnore { get; init; }
	public required bool HasSaveIgnore { get; init; }
	public required bool IsObsolete { get; init; }
	public required bool IsFileLink { get; init; }
	public required bool IsObjectRef { get; init; }
	public required Func<NetworkedObject, object?>? GetValue { get; init; }
	public required Action<NetworkedObject, object?>? SetValue { get; init; }

	public object? Get(NetworkedObject obj) => GetValue!(obj);

	public void Set(NetworkedObject obj, object? value) => SetValue!(obj, value);
}

internal static partial class DatamodelMetaRegistry
{
	private static readonly Dictionary<Type, DatamodelMetaProp[]> _declaredProps = [];
	private static readonly ConcurrentDictionary<Type, Dictionary<string, DatamodelMetaProp>> _resolvedProps = [];

	static DatamodelMetaRegistry()
	{
		RegisterGenerated();
	}

	static partial void RegisterGenerated();

	private static void Register(Type type, DatamodelMetaProp[] props)
	{
		_declaredProps[type] = props;
	}

	private static Dictionary<string, DatamodelMetaProp> Resolve(Type type)
	{
		Dictionary<string, DatamodelMetaProp> map = [];

		for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
		{
			if (_declaredProps.TryGetValue(t, out DatamodelMetaProp[]? props))
			{
				foreach (DatamodelMetaProp prop in props)
				{
					map.TryAdd(prop.Name, prop);
				}
			}
		}

		return map;
	}

	internal static Dictionary<string, DatamodelMetaProp> GetProps(Type type)
	{
		return _resolvedProps.GetOrAdd(type, Resolve);
	}
}
