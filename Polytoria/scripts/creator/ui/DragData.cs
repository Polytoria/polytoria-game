// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Datamodel;
using Polytoria.Utils;
using System.Collections.Generic;

namespace Polytoria.Creator.UI;

[MemoryPackable]
[MemoryPackUnion(0, typeof(InstanceDragData))]
[MemoryPackUnion(1, typeof(FileDragData))]
[MemoryPackUnion(2, typeof(DragData))]
public partial interface IDragDataUnion
{
}

[MemoryPackable]
public partial class InstanceDragData : DragData, IDragDataUnion
{
	[MemoryPackIgnore]
	public Instance[] Instances = [];

	public string[] InstanceIDs = [];

	public new byte[] Serialize()
	{
		List<string> ids = [];
		foreach (Instance item in Instances)
		{
			ids.Add(item.NetworkedObjectID);
		}
		InstanceIDs = [.. ids];
		return SerializeUtils.Serialize<IDragDataUnion>(this);
	}

	[MemoryPackOnDeserialized]
	public void PropagateInstances()
	{
		List<Instance> instances = [];
		foreach (string item in InstanceIDs)
		{
			instances.Add((Instance)World.Current!.GetNetObjectFromID(item)!);
		}
		Instances = [.. instances];
	}
}

[MemoryPackable]
public partial class FileDragData : DragData, IDragDataUnion
{
	public string[] Files = [];
}

[MemoryPackable]
public partial class DragData : IDragDataUnion
{
	public DragType DragType;

	public virtual byte[] Serialize()
	{
		return SerializeUtils.Serialize<IDragDataUnion>(this);
	}

	public static IDragDataUnion? Deserialize(Variant from)
	{

		if (from.VariantType != Variant.Type.PackedByteArray)
			return null;

		var data = from.AsByteArray();

		if (data.Length == 0)
			return null;

		try
		{
			return SerializeUtils.Deserialize<IDragDataUnion>(data);
		}
		catch (MemoryPackSerializationException)
		{
			return null;
		}
	}
}

public enum DragType
{
	None,
	File,
	Instance
}
