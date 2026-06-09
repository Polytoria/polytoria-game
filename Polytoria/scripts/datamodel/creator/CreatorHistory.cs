// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Creator;

[Static("History")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class CreatorHistory : Instance
{
	private const float DeleteTimeoutSec = 60;
	private HistoryAction? _currentAction = null;
	private readonly Stack<HistoryAction> _undoStack = new();
	private readonly Stack<HistoryAction> _redoStack = new();

	public void Undo()
	{
		if (_undoStack.Count == 0)
		{
			return;
		}

		HistoryAction action = _undoStack.Pop();

		foreach (PTCallback callback in action.UndoCallbacks)
		{
			callback?.Invoke();
		}

		_redoStack.Push(action);
		CreatorService.Interface.StatusBar?.SetStatus("Undo " + action.Title);
	}

	public void Redo()
	{
		if (_redoStack.Count == 0)
		{
			return;
		}

		HistoryAction action = _redoStack.Pop();

		foreach (PTCallback callback in action.DoCallbacks)
		{
			callback?.Invoke();
		}

		_undoStack.Push(action);
		CreatorService.Interface.StatusBar?.SetStatus("Redo " + action.Title);
	}

	[ScriptMethod]
	public void NewAction(string title)
	{
		_currentAction = new HistoryAction { Title = title };
	}

	[ScriptMethod]
	public void AddDoCallback(PTCallback callback)
	{
		if (_currentAction == null)
		{
			throw new InvalidOperationException(
				"No action in progress. Call NewAction() first.");
		}

		ArgumentNullException.ThrowIfNull(callback);

		_currentAction.DoCallbacks.Add(callback);
	}

	[ScriptMethod]
	public void AddUndoCallback(PTCallback callback)
	{
		if (_currentAction == null)
		{
			throw new InvalidOperationException(
				"No action in progress. Call NewAction() first.");
		}

		ArgumentNullException.ThrowIfNull(callback);

		_currentAction.UndoCallbacks.Add(callback);
	}

	[ScriptMethod]
	public void CommitAction()
	{
		if (_currentAction == null)
		{
			throw new InvalidOperationException(
				"No action to commit. Call NewAction() first.");
		}

		if (_currentAction.DoCallbacks.Count == 0)
		{
			throw new InvalidOperationException(
				"Action must have at least one 'do' callback.");
		}

		if (_currentAction.UndoCallbacks.Count == 0)
		{
			throw new InvalidOperationException(
				"Action must have at least one 'undo' callback.");
		}

		foreach (PTCallback callback in _currentAction.DoCallbacks)
		{
			callback?.Invoke();
		}

		_undoStack.Push(_currentAction);

		// invalidates redo history
		_redoStack.Clear();
		_currentAction = null;
	}

	/// <summary>
	/// Group the instances and add to history
	/// </summary>
	/// <param name="instances"></param>
	public void GroupInstances(Instance[] instances, GroupAsEnum asWhat = GroupAsEnum.Model)
	{
		Instance[] originalInstances = instances;
		Instance? groupedModel = null;

		NewAction("Group instances");

		AddDoCallback(new((_) =>
		{
			groupedModel = Root.CreatorContext.Selections.GroupInstances(originalInstances, asWhat);
		}));

		AddUndoCallback(new((_) =>
		{
			if (groupedModel != null)
			{
				originalInstances = Root.CreatorContext.Selections.UngroupModel(groupedModel);
			}
		}));

		CommitAction();
	}

	/// <summary>
	/// Ungroup the instances and add to history
	/// </summary>
	/// <param name="instances"></param>
	public void UngroupInstances(Instance[] instances)
	{
		Instance[] originalModels = instances;
		Instance[]? ungroupedChildren = null;

		GroupAsEnum ungroupType = instances.Length > 0 ? GetGroupAsEnum(instances[0]) : GroupAsEnum.Model;

		NewAction("Ungroup instances");

		AddDoCallback(new((_) =>
		{
			ungroupedChildren = Root.CreatorContext.Selections.UngroupModels(originalModels);
		}));

		AddUndoCallback(new((_) =>
		{
			if (ungroupedChildren != null)
			{
				originalModels = [Root.CreatorContext.Selections.GroupInstances(ungroupedChildren, ungroupType)];
			}
		}));

		CommitAction();
	}

	private static GroupAsEnum GetGroupAsEnum(Instance instance) => instance switch
	{
		RigidBody => GroupAsEnum.RigidBody,
		Folder => GroupAsEnum.Folder,
		_ => GroupAsEnum.Model,
	};

	public void ToggleLockedDynamics(Dynamic[] dyns)
	{
		NewAction("Toggle lock dynamics");

		AddDoCallback(new((_) =>
		{
			CreatorSelections.ToggleLockDynamics(dyns);
		}));

		AddUndoCallback(new((_) =>
		{
			CreatorSelections.ToggleLockDynamics(dyns);
		}));

		CommitAction();
	}

	public void DuplicateInstances(Instance[] instances)
	{
		Instance[]? child = null;

		NewAction("Duplicate instances");
		AddDoCallback(new((_) =>
		{
			child = Root.CreatorContext.Selections.DuplicateInstances(instances);
		}));

		AddUndoCallback(new((_) =>
		{
			if (child != null)
			{
				foreach (Instance instance in child)
				{
					instance.Delete();
				}
			}
		}));
		CommitAction();
	}

	public void DeleteInstances(Instance[] instances)
	{
		CreatorSelections selections = Root.CreatorContext.Selections;
		List<DeleteData> deletes = [];
		Instance[]? child = instances;
		bool undoAble = true;
		bool recovered = false;

		foreach (Instance instance in child)
		{
			deletes.Add(new()
			{
				OriginName = instance.Name,
				Object = instance,
				Parent = instance.Parent!,
				Index = instance.Index
			});
		}

		NewAction("Delete instances");
		AddDoCallback(new((_) =>
		{
			recovered = false;
			if (child != null)
			{
				foreach (DeleteData d in deletes)
				{
					d.Object.Parent = d.Object.Root.TemporaryContainer;
					d.Object.Name = d.OriginName + "_DELETED";
					selections.Deselect(d.Object);
				}
			}
		}));

		AddUndoCallback(new((_) =>
		{
			if (!undoAble) return;
			recovered = true;
			selections.DeselectAll();
			foreach (DeleteData d in deletes)
			{
				d.Object.Parent = d.Parent;
				d.Object.Name = d.OriginName;
				d.Parent.MoveChild(d.Object, d.Index);
				selections.Select(d.Object);
			}
		}));

		CommitAction();
		Timer t = new();
		GDNode.AddChild(t, @internal: Node.InternalMode.Back);

		t.Timeout += () =>
		{
			if (recovered) return;
			undoAble = false;
			foreach (DeleteData d in deletes)
			{
				d.Object.Delete();
			}
			t.QueueFree();
		};
		t.Start(DeleteTimeoutSec);
	}

	public void CreateInstances(Instance[] instances, Instance parent)
	{
		foreach (Instance instance in instances)
		{
			instance.Parent = parent;
		}
		CreatorSelections selections = Root.CreatorContext.Selections;
		bool undoAble = true;
		bool recovered = true;

		NewAction("Create instances");

		AddDoCallback(new((_) =>
		{
			recovered = true;
			foreach (Instance instance in instances)
			{
				instance.Parent = parent;
				instance.Name = instance.Name.TrimSuffix("_DELETED");
				selections.Select(instance);
			}
		}));

		AddUndoCallback(new((_) =>
		{
			if (!undoAble) return;
			recovered = false;
			selections.DeselectAll();

			foreach (Instance instance in instances)
			{
				instance.Parent = instance.Root.TemporaryContainer;
				instance.Name += "_DELETED";
				selections.Deselect(instance);
			}
		}));

		CommitAction();

		Timer t = new();
		GDNode.AddChild(t, @internal: Node.InternalMode.Back);

		t.Timeout += () =>
		{
			if (recovered) return;

			undoAble = false;
			foreach (Instance instance in instances)
			{
				instance.Delete();
			}
			t.QueueFree();
		};
		t.Start(DeleteTimeoutSec);
	}

	public void RenameInstance(Instance instance, string newName)
	{
		string oldName = instance.Name;

		NewAction("Rename instance");

		AddDoCallback(new((_) =>
		{
			instance.Name = newName;
		}));

		AddUndoCallback(new((_) =>
		{
			instance.Name = oldName;
		}));

		CommitAction();
	}

	private struct DeleteData
	{
		public string OriginName;
		public Instance Object;
		public Instance Parent;
		public int Index;
	}

	private class HistoryAction
	{
		public string Title { get; set; } = "";
		public List<PTCallback> DoCallbacks { get; set; } = [];
		public List<PTCallback> UndoCallbacks { get; set; } = [];
	}

	public enum GroupAsEnum
	{
		Model,
		Folder,
		RigidBody
	}
}
