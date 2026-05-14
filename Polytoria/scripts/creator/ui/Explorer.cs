// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System.Collections.Generic;
using System.Reflection;

namespace Polytoria.Creator.UI;

public sealed partial class Explorer : TabContainer
{
	private const string ExplorerTabPath = "res://scenes/creator/docks/explorer/explorer_tab.tscn";
	public static Explorer Singleton { get; private set; } = null!;
	public Explorer()
	{
		Singleton = this;
	}

	private static readonly Dictionary<Instance, TreeItem> _instanceToItem = [];
	private static readonly Dictionary<TreeItem, Instance> _itemToInstance = [];
	private static readonly Dictionary<World, ExplorerTab> _gameToTab = [];
	private readonly Dictionary<Instance, List<Instance>> _pendingChildren = [];

	// Flag to prevent recursive selection
	private static bool _isUpdatingSelection = false;

	public static World? CurrentRoot { get; private set; }

	public void SwitchTo(World? game)
	{
		CurrentRoot = game;
		CurrentTab = game == null ? -1 : GetTabIdxFromControl(GetTabFromRoot(game));
	}

	public void Insert(Instance instance)
	{
		// If excluded from explorer, return
		if (instance.GetType().IsDefined(typeof(ExplorerExcludeAttribute)))
		{
			return;
		}

		// Check if any parent in the hierarchy is excluded
		if (HasExcludedAncestor(instance))
		{
			return;
		}

		if (instance.Parent != null && !_instanceToItem.ContainsKey(instance.Parent))
		{
			// Parent doesn't exist yet, queue this instance
			if (!_pendingChildren.ContainsKey(instance.Parent))
				_pendingChildren[instance.Parent] = [];

			_pendingChildren[instance.Parent].Add(instance);
			return;
		}

		// If already exists, return
		if (_instanceToItem.ContainsKey(instance)) return;

		ExplorerTree? rootTree = GetTreeFromRoot(instance.Root);
		TreeItem item;
		if (instance is World game)
		{
			ExplorerTab tree = Globals.CreateInstanceFromScene<ExplorerTab>(ExplorerTabPath);
			_gameToTab[game] = tree;
			tree.Root = game;
			tree.Tree.Root = game;
			tree.Tree.MultiSelected += OnMultiSelect;
			tree.Tree.ItemEdited += OnItemEdited;

			item = tree.Tree.CreateItem();
			AddChild(tree);
			rootTree = tree.Tree;
		}
		else
		{
			if (instance.Parent == null)
			{
				PT.Print(instance.Name, " no parent");
				return;
			}
			TreeItem parentItem = _instanceToItem[instance.Parent];
			item = parentItem.CreateChild();
			item.Collapsed = true;

			// Renameable
			if (!instance.GetType().IsDefined(typeof(StaticAttribute)) && instance is not Datamodel.Script)
			{
				item.SetEditable(0, true);
			}
		}

		item.SetIcon(0, Globals.LoadIcon(instance.ClassName));
		item.SetText(0, instance.Name);

		_instanceToItem[instance] = item;
		_itemToInstance[item] = instance;

		RefreshLinked(instance);
		if (instance is Dynamic dyn)
		{
			RefreshLocked(dyn);
		}

		if (rootTree != null)
		{
			rootTree.InstanceToItem[instance] = item;
			rootTree.ItemToInstance[item] = instance;
		}

		// Check if any children were waiting for this instance
		if (_pendingChildren.TryGetValue(instance, out List<Instance>? children))
		{
			foreach (Instance child in children)
			{
				Insert(child); // recursively insert queued children
			}
			_pendingChildren.Remove(instance);
		}
	}

	private static bool HasExcludedAncestor(Instance instance)
	{
		Instance? current = instance.Parent;
		while (current != null)
		{
			if (current.GetType().IsDefined(typeof(ExplorerExcludeAttribute)))
			{
				return true;
			}
			current = current.Parent;
		}
		return false;
	}


	public static ExplorerTree? GetCurrentTree()
	{
		if (CurrentRoot == null) { return null; }
		return GetTreeFromRoot(CurrentRoot);
	}

	public static ExplorerTree? GetTreeFromRoot(World root)
	{
		if (root == null) return null;
		if (_gameToTab.TryGetValue(root, out ExplorerTab? tab))
		{
			return tab.Tree;
		}
		return null;
	}

	public static ExplorerTab? GetTabFromRoot(World root)
	{
		if (root == null) return null;
		if (_gameToTab.TryGetValue(root, out ExplorerTab? tab))
		{
			return tab;
		}
		return null;
	}

	public static void Remove(Instance instance)
	{
		if (_instanceToItem.Remove(instance, out TreeItem? item))
		{
			_itemToInstance.Remove(item);
			ExplorerTab? tree = GetTabFromRoot(instance.Root);
			if (tree != null)
			{
				tree.Tree.InstanceToItem.Remove(instance);
				tree.Tree.ItemToInstance.Remove(item);
			}

			if (instance is World game)
			{
				_gameToTab.Remove(game);
				Properties.Remove(game);
				if (tree != null)
				{
					tree.QueueFree();
					tree.Dispose();
				}
			}
			else if (IsInstanceValid(item))
			{
				item.Free();
			}
		}
	}

	public static Instance? GetInstanceFromTreeItem(TreeItem item)
	{
		if (_itemToInstance.TryGetValue(item, out Instance? instance))
		{
			return instance;
		}
		return null;
	}

	public static TreeItem? GetTreeItemFromInstance(Instance item)
	{
		if (_instanceToItem.TryGetValue(item, out TreeItem? instance))
		{
			return instance;
		}
		return null;
	}

	public static void Rename(Instance instance)
	{
		if (_instanceToItem.TryGetValue(instance, out TreeItem? item))
		{
			// lastly, set text
			Callable.From(() =>
			{
				if (instance.IsDeleted) return;
				if (!Node.IsInstanceValid(item)) return;

				item.SetText(0, instance.Name);
			}).CallDeferred();
		}
	}

	public static void Move(Instance instance, int index)
	{
		if (!_instanceToItem.TryGetValue(instance, out TreeItem? item))
			return;

		bool collapseState = item.Collapsed;
		TreeItem parent = item.GetParent();
		int childCount = parent.GetChildCount();
		int currentTreeIndex = item.GetIndex();

		index = Mathf.Clamp(index, 0, childCount - 1);

		if (currentTreeIndex == index)
			return;

		if (index >= childCount)
		{
			// Move to the end
			TreeItem last = parent.GetChild(childCount - 1);
			item.MoveAfter(last);
		}
		else
		{
			int targetIndex = index;
			if (currentTreeIndex < index)
			{
				targetIndex++;
			}

			if (targetIndex >= childCount)
			{
				TreeItem last = parent.GetChild(childCount - 1);
				item.MoveAfter(last);
			}
			else
			{
				TreeItem sibling = parent.GetChild(targetIndex);
				item.MoveBefore(sibling);
			}
		}

		item.Collapsed = collapseState;
		ExplorerTree tree = (ExplorerTree)item.GetTree();
		tree.ScrollToItemFrame(item);
	}


	public static void Reparent(Instance instance, Instance to)
	{
		if (!_instanceToItem.TryGetValue(instance, out TreeItem? item))
			return;

		if (!_instanceToItem.TryGetValue(to, out TreeItem? newParent))
		{
			item.SetMeta("_force_invisible", true);
			RefreshTreeItemVisibility(item);
			return;
		}

		TreeItem? oldParent = item.GetParent();
		oldParent?.RemoveChild(item);
		newParent.AddChild(item);
		item.SetMeta("_force_invisible", false);
		RefreshTreeItemVisibility(item);
	}

	internal static void RefreshTreeItemVisibility(TreeItem item)
	{
		bool forceInvisible = item.HasMeta("_force_invisible") && item.GetMeta("_force_invisible").AsBool();
		bool modelNonEditable = item.HasMeta("_model_non_editable") && item.GetMeta("_model_non_editable").AsBool();

		item.Visible = !forceInvisible && !modelNonEditable;
		if (item.Visible && GetInstanceFromTreeItem(item) is Instance i)
		{
			// Refresh instance tree item if just became visible (rename doesn't work when invisible for some reason DUH!!!)
			Rename(i);
		}
	}

	public static void Select(Instance instance)
	{
		if (_instanceToItem.TryGetValue(instance, out TreeItem? item))
		{
			if (_isUpdatingSelection)
				return;

			_isUpdatingSelection = true;
			try
			{
				item.Select(0);

				// Uncollapse tree to reveal this item
				TreeItem? parent = item.GetParent();
				while (parent != null)
				{
					parent.Collapsed = false;
					parent = parent.GetParent();
				}

				ExplorerTree tree = (ExplorerTree)item.GetTree();
				tree.ScrollToItemFrame(item);
			}
			finally
			{
				_isUpdatingSelection = false;
			}
		}
	}

	public static void RefreshLinked(Instance instance)
	{
		if (_instanceToItem.TryGetValue(instance, out TreeItem? item))
		{
			if (item.GetButtonById(0, 1) == -1 && instance.LinkedModel != null)
			{
				item.AddButton(0, Globals.LoadUIIcon("link"), 1, tooltipText: $"Linked Model ({instance.LinkedModel.LinkedPath})");
			}
			else if (item.GetButtonById(0, 1) != -1 && instance.LinkedModel == null)
			{
				item.EraseButton(0, item.GetButtonById(0, 1));
			}

			if (instance.LinkedModel != null)
			{
				foreach (Instance ch in instance.ModelChilds.Values)
				{
					TreeItem? i = GetTreeItemFromInstance(ch);
					if (i != null)
					{
						MarkTreeItemEditable(i, instance.EditableChildren);
					}
				}
			}

			if (instance.ModelRoot != null)
			{
				MarkTreeItemEditable(item, instance.ModelRoot.EditableChildren);
			}
			else
			{
				item.SetMeta("_model_non_editable", false);
				RefreshTreeItemVisibility(item);
				item.ClearCustomColor(0);
			}
		}
	}

	public static void RefreshLocked(Dynamic dyn)
	{
		if (_instanceToItem.TryGetValue(dyn, out TreeItem? item))
		{
			if (item.GetButtonById(0, 2) == -1 && dyn.Locked)
			{
				item.AddButton(0, Globals.LoadUIIcon("lock"), 2, tooltipText: "Locked");
			}
			else if (item.GetButtonById(0, 2) != -1 && !dyn.Locked)
			{
				item.EraseButton(0, item.GetButtonById(0, 2));
			}
		}
	}

	private static void MarkTreeItemEditable(TreeItem item, bool to)
	{
		item.SetMeta("_model_non_editable", !to);
		RefreshTreeItemVisibility(item);
		if (to)
		{
			item.SetCustomColor(0, new("#FFBC58"));
		}
		else
		{
			item.ClearCustomColor(0);
		}
	}

	public static void Deselect(Instance instance)
	{
		if (_instanceToItem.TryGetValue(instance, out TreeItem? item) && item != null)
		{
			if (_isUpdatingSelection)
				return;

			_isUpdatingSelection = true;
			try
			{
				item.Deselect(0);
			}
			finally
			{
				_isUpdatingSelection = false;
			}
		}
	}

	private void OnMultiSelect(TreeItem item, long _, bool selected)
	{
		if (_isUpdatingSelection)
			return;

		Instance instance = _itemToInstance[item];

		if (selected && !Input.IsKeyPressed(Key.Ctrl))
		{
			CurrentRoot?.CreatorContext.Selections.DeselectAll();
		}

		_isUpdatingSelection = true;
		try
		{
			if (selected)
			{
				CurrentRoot?.CreatorContext.Selections.Select(instance);
			}
			else
			{
				CurrentRoot?.CreatorContext.Selections.Deselect(instance);
			}
		}
		finally
		{
			_isUpdatingSelection = false;
		}
	}

	private void OnItemEdited()
	{
		ExplorerTree currentTree = GetCurrentTree()!;

		TreeItem targetItem = currentTree.GetEdited();
		Instance? instance = GetInstanceFromTreeItem(targetItem);

		if (instance == null) return;
		string newName = targetItem.GetText(0);
		try
		{
			currentTree.Root.CreatorContext.History.RenameInstance(instance, newName);
		}
		catch (System.Exception ex)
		{
			CreatorService.Interface.PopupAlert(ex.Message, "Error renaming instance");
		}

		// Lastly update the name in-case name got rejected
		Callable.From(() =>
		{
			targetItem.SetText(0, instance.Name);
		}).CallDeferred();
	}
}
