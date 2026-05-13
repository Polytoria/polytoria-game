// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System.Collections.Generic;
using Polytoria.Shared;
using System;
using Polytoria.Scripting;

namespace Polytoria.Scripting;

public partial class ScriptSharedTable : IScriptObject
{
	internal record class Entry
	{
		public object Key { get; set; }
		public object Value { get; set; }
		public Entry(object Key, object Value)
		{
			this.Key = Key;
			this.Value = Value;
		}
	}
	internal Dictionary<object, LinkedListNode<Entry>> NodeDict = [];
	internal LinkedList<Entry> KeyLinkedList = new();

	[ScriptMethod]
	public void Clear()
	{
		NodeDict.Clear();
		KeyLinkedList.Clear();
	}
	[ScriptMethod]
	public void Remove(string key)
	{
		if (NodeDict.Remove(key, out LinkedListNode<Entry> node))
		{
			KeyLinkedList.Remove(node);
		}
	}

	[ScriptMethod]
	public void ClearPrefix(string prefix)
	{
		foreach ((object key, LinkedListNode<Entry> node) in NodeDict)
		{
			if (key is string strk && strk.StartsWith(prefix))
			{
				NodeDict.Remove(key);
				KeyLinkedList.Remove(node);
			}
		}
	}

	[ScriptMethod]
	public void ClearSuffix(string suffix)
	{
		foreach ((object key, LinkedListNode<Entry> node) in NodeDict)
		{
			if (key is string strk && strk.EndsWith(suffix))
			{
				NodeDict.Remove(key);
				KeyLinkedList.Remove(node);
			}
		}
	}
	public (object?, object?) Next(object? index)
	{
		if (index != null)
		{
			if (NodeDict.TryGetValue(index, out LinkedListNode<Entry> node))
			{
				return node.Next?.Value is { } pair ? (pair.Key, pair.Value) : (null, null);
			}
			else
			{
				throw new Exception("invalid key to 'next'");
			}
		}
		else
		{
			return (KeyLinkedList.First?.Value is { } pair) ? (pair.Key, pair.Value) : (null, null);
		}
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Index)]
	public object? Index(object index)
	{
		if (NodeDict.TryGetValue(index, out LinkedListNode<Entry> node))
		{
			return node.Value;
		}
		return null;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.NewIndex)]
	public void NewIndex(object index, object? val)
	{
		if (NodeDict.TryGetValue(index, out LinkedListNode<Entry> node))
		{
			if (val != null)
			{

				node.Value.Value = val;
			}
			else
			{
				NodeDict.Remove(index);
				KeyLinkedList.Remove(node);
			}
		}
		else if (val != null)
		{
			NodeDict.Add(index, KeyLinkedList.AddLast(new Entry(index, val)));
		}
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Iter)]
	public static IEnumerable<(object, object)> Iter(ScriptSharedTable sTable)
	{
		foreach (Entry kvp in sTable.KeyLinkedList)
		{
			yield return (kvp.Key, kvp.Value);
		}
	}
}
