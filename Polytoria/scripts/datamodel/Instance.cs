// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

#if CREATOR
using Polytoria.Creator.UI;
#endif
using Polytoria.Datamodel.Resources;
using Polytoria.Formats;
using Polytoria.Scripting;
using Polytoria.Networking.Synchronizers;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Polytoria.Datamodel;

[Abstract]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public partial class Instance : NetworkedObject
{
	private const float WaitChildWarningSec = 10.0f;

	private string? _legacyName = null;
	private string[] _tags = [];
	private bool _archivable;
	internal readonly Dictionary<string, Instance> legacyChild = [];
	private bool _isHidden = false;
	private bool _editableChildren;
	private FileLinkAsset? _linkedModel;
	private Instance? _modelRoot;

	internal Dictionary<string, Instance> NameToChild = [];
	internal List<Instance> Children = [];
	internal int Index = 0;

	[ScriptProperty, IgnoreCleanup]
	public Instance? Parent
	{
		get
		{
			if (this is World)
			{
				return null;
			}

			return (Instance?)NetworkParent;
		}
		set
		{
			if (GetType().IsDefined(typeof(StaticAttribute), false))
			{
				throw new InvalidOperationException("Cannot reparent a static class");
			}
			if (ModelRoot != null && value != null)
			{
				if (value != ModelRoot && !value.IsDescendantOf(ModelRoot))
				{
					throw new InvalidOperationException("Child cannot be reparented outside the root model");
				}
			}

			ParentOverride = value;
		}
	}

	/// <summary>
	/// Set parent without checks
	/// </summary>
	internal Instance? ParentOverride
	{
		get
		{
			if (this is World)
			{
				return null;
			}

			return (Instance?)NetworkParent;
		}
		set
		{
			NetworkedObject? oldParent = Parent;

			bool isTemp = false;
			bool chunkOverride = false;
			bool oldParentIsTemp = false;
			NetworkedObject[]? descendants = GetReplicateDescendants();

			if (Root != null && value != null)
			{
				oldParentIsTemp = oldParent == Root.TemporaryContainer;
				chunkOverride = Root.Network.IsServer && oldParentIsTemp && !AutoReplicate;
				isTemp = value == Root.TemporaryContainer || value.IsDescendantOf(Root.TemporaryContainer);

				if (IsInTemporary && !isTemp)
				{
					if (chunkOverride)
					{
						NetworkReplicateSync.MarkChunkOverride([this, .. descendants]);
					}
				}
			}

			NetworkParent = value;

			// If set parent, invoke ready
			if (!isTemp && Root != null)
			{
				// Broadcast chunk if is replicated from Temporary
				if (chunkOverride)
				{
					// Call on next frame for descendants to finish initialize
					// this is kinda hacky
					Callable.From(() =>
					{
						Root.Network.ReplicateSync.BroadcastChunk([this, .. descendants]);
					}).CallDeferred();
				}

				// Rerun name enforcement in case of reparenting from temp
				foreach (NetworkedObject item in descendants)
				{
					item.ReenforceName();
				}

				// NOTE: It's kinda verbose having AutoInvokeReadyOnParent too, but AutoInvokeReady solely won't work with instance created with .New()
				if (!IsPropReady && AutoInvokeReadyOnParent)
				{
					InvokePropReady();
				}
			}
		}
	}

	[CloneInclude, ScriptLegacyProperty("Name"), SyncVar]
	public string LegacyName
	{
		get => _legacyName ?? Name;
		set
		{
			RemoveLegacyNameFromParent();

			_legacyName = value;
			NameOverride = _legacyName;

			AddLegacyNameToParent();
		}
	}

	public string LuaPath
	{
		get
		{
			Instance? instance = this;
			List<string> ancestors = [];

			while (instance != null)
			{
				if (instance is World)
				{
					ancestors.Add("world");
				}
				else
				{
					ancestors.Add(instance.Name);
				}
				instance = instance.Parent;
			}

			ancestors.Reverse();
			return string.Join('.', ancestors);
		}
	}

	public bool IsHidden
	{
		get => _isHidden;
		set
		{
			if (_isHidden == value)
			{
				return;
			}

			_isHidden = value;
			InvokeHiddenChanged();
			OnPropertyChanged();
		}
	}

	[CloneInclude]
	public FileLinkAsset? LinkedModel
	{
		get => _linkedModel;
		set
		{
			if (_linkedModel != null && _linkedModel != value)
			{
				_linkedModel.UnlinkFrom(this);
			}
			_linkedModel = value;
			if (_linkedModel != null)
			{
				_linkedModel.LinkTo(this);
#if CREATOR
				Explorer.RefreshLinked(this);
			}
			else
			{
				Explorer.RefreshLinked(this);
#endif
			}
		}
	}

	public Instance? ModelRoot
	{
		get => _modelRoot;
		set
		{
			if (_modelRoot != null && _modelRoot != value)
			{
				_modelRoot.UnregisterModelChild(this);
			}
			_modelRoot = value;
			_modelRoot?.RegisterModelChild(this);
#if CREATOR
			Explorer.RefreshLinked(this);
#endif
		}
	}


	public Dictionary<string, Instance> ModelChilds = [];

	[Editable(IsHidden = true), DefaultValue(false)]
	public bool EditableChildren
	{
		get => _editableChildren;
		set
		{
			_editableChildren = value;
#if CREATOR
			Explorer.RefreshLinked(this);
#endif
		}
	}

	[Editable(IsHidden = true), ScriptProperty]
	public string[] Tags
	{
		get => _tags;
		set
		{
			if (_tags.SequenceEqual(value))
			{
				return;
			}

			_tags = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Archivable
	{
		get => _archivable;
		set
		{
			if (_archivable == value)
			{
				return;
			}

			_archivable = value;
			OnPropertyChanged();
		}
	}

	public bool IsInTemporary => IsDescendantOfClass<Temporary>();

	[ScriptProperty] public PTSignal<Instance> ChildAdded { get; private set; } = new();
	[ScriptProperty] public PTSignal<Instance> ChildRemoved { get; private set; } = new();
	[ScriptProperty] public PTSignal<Instance> ChildDeleting { get; private set; } = new();
	[ScriptProperty] public PTSignal<Instance> ChildDeleted { get; private set; } = new();

	internal void AddLegacyNameToParent()
	{
		if (_legacyName != null && Parent != null)
		{
			Parent.legacyChild.TryAdd(_legacyName, this);
		}
	}

	internal void RemoveLegacyNameFromParent()
	{
		if (_legacyName != null && Parent != null)
		{
			Parent.legacyChild.Remove(_legacyName);
		}
	}

	internal void AddNameToParent()
	{
		if (Name != null && Parent != null)
		{
			Parent.NameToChild.TryAdd(Name, this);
		}
	}

	internal void RemoveNameFromParent()
	{
		if (Parent != null && Name != null)
		{
			Parent.NameToChild.Remove(Name);
		}
	}

	[ScriptMethod]
	public Instance[] GetDescendants()
	{
		List<Instance> instances = [];

		foreach (Instance child in Children)
		{
			instances.Add(child);

			// Recursively add descendants
			instances.AddRange(child.GetDescendants());
		}

		return [.. instances];
	}

	[ScriptMethod]
	public Instance? FindChild(string name)
	{
		if (NameToChild.TryGetValue(name, out Instance? instance)) return instance;

		foreach (Instance child in Children)
		{
			if (child.Name == name)
			{
				NameToChild[name] = child;
				return child;
			}
		}

		return null;
	}

	public T? FindChild<T>(string name) where T : Instance
	{
		foreach (Instance child in Children)
		{
			if (child.Name == name)
			{
				return (T)child;
			}
		}

		return default;
	}

	[ScriptMethod]
	public async Task<Instance?> WaitChild(string name, float? timeoutSec = null)
	{
		Instance? child = FindChild(name);
		if (child != null)
		{
			return child;
		}
		TaskCompletionSource<Instance> tcs = new();
		void OnChildAdded(Instance newChild)
		{
			if (newChild.Name == name)
			{
				tcs.TrySetResult(newChild);
				ChildAdded.Disconnect(OnChildAdded);
			}
		}
		ChildAdded.Connect(OnChildAdded);

		using CancellationTokenSource warningCts = new();

		Task warningTask = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(WaitChildWarningSec), warningCts.Token);
				if (!tcs.Task.IsCompleted)
				{
					PT.PrintWarn($"Possible infinite yield: WaitChild has been waiting for '{name}' for more than {WaitChildWarningSec}s.");
				}
			}
			catch (OperationCanceledException) { }
		});

		if (timeoutSec.HasValue)
		{
			Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSec.Value));
			Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
			ChildAdded.Disconnect(OnChildAdded);
			// cancel warning if completed
			warningCts.Cancel();
			if (completedTask == timeoutTask)
			{
				return null;
			}
			return await tcs.Task;
		}

		try
		{
			return await tcs.Task;
		}
		finally
		{
			// cancel warning if completed
			warningCts.Cancel();
		}
	}

	public async Task<T?> WaitChild<T>(string name, float? timeoutSec = null) where T : Instance
	{
		return (T?)await WaitChild(name, timeoutSec);
	}


	[ScriptLegacyMethod("FindChild")]
	public Instance? LegacyFindChild(string name)
	{
		if (legacyChild.TryGetValue(name, out Instance? val))
		{
			return val;
		}

		foreach (Instance item in GetChildren())
		{
			if (item.LegacyName != null && item.LegacyName == name)
			{
				legacyChild[item.LegacyName] = item;
				return item;
			}
		}

		return null;
	}

	[ScriptMethod]
	public Instance? FindChildByClass(string className)
	{
		foreach (Instance child in GetChildren())
		{
			if (child.ClassName == className)
			{
				return child;
			}
		}

		return null;
	}

	[ScriptMethod]
	public Instance? FindChildWithTag(string tag)
	{
		foreach (Instance child in GetChildren())
		{
			if (child.HasTag(tag))
			{
				return child;
			}
		}

		return null;
	}

	[ScriptMethod]
	public Instance? FindDescendant(string path)
	{
		string[] separatedPath = path.Split('.');
		Instance? inst = this;

		foreach (string segment in separatedPath)
		{
			inst = inst.FindChild(segment);
			if (inst == null)
			{
				return null;
			}
		}

		return inst;
	}

	[ScriptMethod]
	public Instance[] GetChildrenWithTag(string tag)
	{
		List<Instance> childs = [];
		foreach (Instance child in GetChildren())
		{
			if (child.HasTag(tag))
			{
				childs.Add(child);
			}
		}

		return [.. childs];
	}

	[ScriptMethod]
	public Instance[] GetDescendantsWithTag(string tag)
	{
		List<Instance> des = [];
		foreach (Instance child in GetDescendants())
		{
			if (child.HasTag(tag))
			{
				des.Add(child);
			}
		}

		return [.. des];
	}

	[ScriptMethod]
	public Instance? FindAncestorByClass(string className)
	{
		Instance? parent = Parent;
		while (parent != null)
		{
			Type? currentType = parent.GetType();
			// Check current type and all base types
			while (currentType != null)
			{
				if (currentType.Name == className)
					return parent;
				currentType = currentType.BaseType;
			}
			parent = parent.Parent;
		}
		return null;
	}

	[ScriptLegacyMethod("FindChildByClass")]
	public Instance? LegacyFindChildByClass(string className)
	{
		foreach (Instance child in GetChildren())
		{
			if (child.ClassName == XmlFormat.ConvertClassName(className))
			{
				return child;
			}
		}

		return null;
	}

	[ScriptMethod]
	public Instance? FindChildByIndex(int index)
	{
		if (index < 0 || index >= Children.Count) return null;
		return Children[index];
	}

	[ScriptMethod]
	public void MoveChild(Instance child, int index)
	{
		InternalMoveChild(child.Name, index);

		// Root may not be available
		if (Root != null && Root.Network != null && Root.Network.IsServer)
		{
			Rpc(nameof(NetMoveChild), child.Name, index);
		}
	}

	[NetRpc(Networking.AuthorityMode.Authority, TransferMode = Networking.TransferMode.Reliable)]
	private void NetMoveChild(string name, int index)
	{
		if (RemoteSenderId != 1) return;
		InternalMoveChild(name, index);
	}

	internal void InternalMoveChild(string childName, int index)
	{
		Instance? child = FindChild(childName);
		if (child == null) return;

		int childIndex = Children.IndexOf(child);

		if (childIndex == -1)
		{
			throw new InvalidOperationException("Child is not a child of this instance");
		}

		int targetIndex = Math.Clamp(index, 0, Children.Count - 1);

		if (childIndex == targetIndex)
		{
			return;
		}

		Children.RemoveAt(childIndex);
		Children.Insert(targetIndex, child);

		for (int i = 0; i < Children.Count; i++)
		{
			Children[i].Index = i;
		}

#if CREATOR
		Explorer.Move(child, index);
#endif
		if (child.GDNode != null)
		{
			GDNode?.MoveChild(child.GDNode, index);
		}

		child.PostIndexMove();
	}

#if CREATOR
	public virtual void CreatorSelected() { }

	public virtual void CreatorDeselected() { }
#endif

	public override void EnterTree()
	{
		IsHidden = IsDescendantOfClass<HiddenBase>();

#if CREATOR
		Explorer.Singleton?.Insert(this);
#endif
		base.EnterTree();
	}

	public override void Init()
	{
		base.Init();
#if CREATOR
		Renamed.Connect(OnRenamed);
#endif
	}

#if CREATOR
	private void OnRenamed()
	{
		Explorer.Rename(this);
	}
#endif

	public override void PostReparent()
	{
		base.PostReparent();
#if CREATOR
		if (Parent != null)
		{
			Explorer.Reparent(this, Parent);
		}
#endif
	}

	/// <summary>
	/// Fires when this object's index has been moved
	/// </summary>
	public virtual void PostIndexMove() { }

	[ScriptMethod]
	public Instance[] GetChildren()
	{
		return [.. Children];
	}

	[ScriptMethod]
	public Instance[] GetChildrenOfClass(string className)
	{
		List<Instance> instances = [];

		foreach (Instance item in GetChildren())
		{
			if (item.ClassName == className)
			{
				instances.Add(item);
			}
		}

		return [.. instances];
	}

	[ScriptLegacyMethod("GetChildrenOfClass")]
	public Instance[] LegacyGetChildrenOfClass(string className)
	{
		return GetChildrenOfClass(XmlFormat.ConvertClassName(className));
	}

	public T[] GetChildrenOfClass<T>() where T : Instance
	{
		List<T> instances = [];
		foreach (Instance item in GetChildren())
		{
			if (item is T typedItem)
			{
				instances.Add(typedItem);
			}
		}
		return [.. instances];
	}

	public override void Ready()
	{
		foreach (Instance n in GetChildren())
		{
			legacyChild.TryAdd(n.LegacyName, n);
		}

		ChildRemoved.Connect(OnChildRemoved);
		base.Ready();
	}

	public override void PreDelete()
	{
		base.PreDelete();
		ModelRoot?.UnregisterModelChild(this);
		ChildRemoved.Disconnect(OnChildRemoved);
		Children.Clear();
#if CREATOR
		Renamed.Disconnect(OnRenamed);
		Explorer.Remove(this);
		Root?.CreatorContext?.Selections.Deselect(this);
#endif
	}

	private void OnChildRemoved(Instance obj)
	{
		legacyChild.Remove(obj.LegacyName);
	}

	[ScriptMethod]
	public bool IsAncestorOf(Instance instance)
	{
		Instance? parent = instance.Parent;
		while (parent != null)
		{
			if (parent == this)
				return true;
			parent = parent.Parent;
		}
		return false;
	}

	[ScriptMethod]
	public bool IsDescendantOf(Instance instance)
	{
		Instance? parent = Parent;
		while (parent != null)
		{
			if (parent == instance)
				return true;
			parent = parent.Parent;
		}
		return false;
	}

	[ScriptMethod]
	public bool IsDescendantOfClass(string className)
	{
		Instance? parent = Parent;

		while (parent != null)
		{
			Type? currentType = parent.GetType();
			while (currentType != null && currentType != typeof(object))
			{
				if (currentType.Name == className)
					return true;
				if (currentType == typeof(NetworkedObject))
					break;
				currentType = currentType.BaseType;
			}
			parent = parent.Parent;
		}
		return false;
	}

	public bool IsDescendantOfClass<T>()
	{
		return IsDescendantOfClass(typeof(T).Name);
	}

	[ScriptMethod("New")]
	public static Instance? ScriptNew([ScriptingCaller] Script caller, string className, Instance? parent = null)
	{
		return NewStatic(className, parent, caller.Root);
	}

	public Instance? New(string className, Instance? parent = null)
	{
		return NewStatic(className, parent, Root);
	}

	public static Instance? NewStatic(string className, Instance? parent = null, World? root = null)
	{
		NetworkedObject obj = NewInternal(className, parent, root);
		if (obj is not Instance)
		{
			obj.Destroy();
			return null;
		}
		return (Instance)obj;
	}

	[ScriptLegacyMethod("New")]
	public static Instance? LegacyNew([ScriptingCaller] Script caller, string className, Instance? parent = null)
	{
		Instance? i = NewStatic(XmlFormat.ConvertClassName(className), parent, caller.Root);
		if (i != null)
		{
			if (parent == null)
			{
				i.Parent = caller.Root.Environment;
			}
			return i;
		}
		return null;
	}

	public Instance? GetModelChildFromID(string objID)
	{
		return ModelChilds.TryGetValue(objID, out Instance? instance) ? instance : null;
	}

#if CREATOR
	public void SaveModel()
	{
		if (LinkedModel == null) return;
		Root.LinkedSession.SaveModel(this, LinkedModel.LinkedPath!);
	}

	public void DetachModel()
	{
		LinkedModel = null;

		foreach (Instance item in GetDescendants())
		{
			if (item.ModelRoot == this)
			{
				item.ModelRoot = null;
			}
		}
	}
#endif

	public void RegisterModelChild(Instance i)
	{
		ModelChilds.Add(i.ObjectID, i);
	}

	public void UnregisterModelChild(Instance i)
	{
		ModelChilds.Remove(i.ObjectID);
	}

	[ScriptMethod]
	public void AddTag(string tag)
	{
		List<string> tags = [.. Tags];
		tags.Add(tag);
		Tags = [.. tags];
	}

	[ScriptMethod]
	public void RemoveTag(string tag)
	{
		List<string> tags = [.. Tags];
		tags.Remove(tag);
		Tags = [.. tags];
	}

	[ScriptMethod]
	public bool HasTag(string tag)
	{
		return Tags.Contains(tag);
	}

	private void InvokeHiddenChanged()
	{
		// Put in callable so when reparented to another hidden it doesn't stack
		bool myVal = _isHidden;
		Callable.From(() =>
		{
			if (_isHidden != myVal) return;
			if (IsDeleted) return;
			HiddenChanged(_isHidden);
		}).CallDeferred();
	}

	[ScriptMethod]
	public void Reparent(Instance to)
	{
		Parent = to;
	}

	[ScriptMethod]
	public new Instance? GetParent()
	{
		return Parent;
	}

	[ScriptMethod]
	public void SetParent(Instance newParent)
	{
		Parent = newParent;
	}

	public virtual void HiddenChanged(bool to) { }
}
