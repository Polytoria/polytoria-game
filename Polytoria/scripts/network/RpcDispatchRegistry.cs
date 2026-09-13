// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Polytoria.Networking;

public sealed class RpcDispatchEntry
{
	public required string Name { get; init; }
	public required AuthorityMode AuthorMode { get; init; }
	public required TransferMode TransferMode { get; init; }
	public required int TransferChannel { get; init; }
	public required bool CallLocal { get; init; }
	public required bool AllowToServerOnly { get; init; }
	public required Action<NetworkedObject, object?[]?> InvokeLocal { get; init; }
	public required Action<NetworkedObject, byte[][]> InvokeWire { get; init; }
}

internal static partial class RpcDispatchRegistry
{
	private static readonly Dictionary<Type, RpcDispatchEntry[]> _declaredEntries = [];
	private static readonly ConcurrentDictionary<Type, RpcTypeMap> _typeMaps = [];

	static RpcDispatchRegistry()
	{
		RegisterGenerated();
	}

	static partial void RegisterGenerated();

	internal static void RegisterDeclared(Type type, RpcDispatchEntry[] entries)
	{
		Array.Sort(entries, static (a, b) => string.CompareOrdinal(a.Name, b.Name));
		_declaredEntries[type] = entries;
	}

	internal static void WarmType(Type type)
	{
		_typeMaps.GetOrAdd(type, static t => BuildTypeMap(t));
	}

	internal static RpcDispatchEntry GetEntry(Type type, string methodName, out int methodId)
	{
		RpcTypeMap map = _typeMaps.GetOrAdd(type, static t => BuildTypeMap(t));

		if (!map.NameToId.TryGetValue(methodName, out methodId))
			throw new Exception($"No RPC method found with name '{methodName}'");

		return map.IdToEntry[methodId];
	}

	internal static int GetMethodId(Type type, string methodName)
	{
		RpcTypeMap map = _typeMaps.GetOrAdd(type, static t => BuildTypeMap(t));

		if (!map.NameToId.TryGetValue(methodName, out int methodId))
			throw new Exception($"No RPC method found with name '{methodName}'");

		return methodId;
	}

	internal static RpcDispatchEntry GetEntry(Type type, int methodId)
	{
		RpcTypeMap map = _typeMaps.GetOrAdd(type, static t => BuildTypeMap(t));

		if (!map.IdToEntry.TryGetValue(methodId, out RpcDispatchEntry? entry))
			throw new Exception($"No RPC method found with id '{methodId}'");

		return entry;
	}

	private static RpcTypeMap BuildTypeMap(Type type)
	{
		Dictionary<string, int> nameToId = [];
		Dictionary<int, RpcDispatchEntry> idToEntry = [];
		int id = 0;

		for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
		{
			if (!_declaredEntries.TryGetValue(current, out RpcDispatchEntry[]? entries))
				continue;

			foreach (RpcDispatchEntry entry in entries)
			{
				if (nameToId.ContainsKey(entry.Name))
					continue;

				nameToId[entry.Name] = id;
				idToEntry[id] = entry;
				id++;
			}
		}

		return new RpcTypeMap(nameToId, idToEntry);
	}

	private sealed class RpcTypeMap(Dictionary<string, int> nameToId, Dictionary<int, RpcDispatchEntry> idToEntry)
	{
		public readonly Dictionary<string, int> NameToId = nameToId;
		public readonly Dictionary<int, RpcDispatchEntry> IdToEntry = idToEntry;
	}
}
