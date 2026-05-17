// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator;
using Polytoria.Creator.UI;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;
using Polytoria.Shared.Misc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Polytoria.Datamodel.Creator.CreatorHistory;

namespace Polytoria.Datamodel.Creator;

[Static("Selections")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class CreatorSelections : Instance
{
	public readonly List<Instance> SelectedInstances = [];
	private bool _propertiesDirty = false;
	private TaskCompletionSource<Instance>? _pickTcs;

	[ScriptProperty] public PTSignal<Instance> Selected { get; private set; } = new();
	[ScriptProperty] public PTSignal<Instance> Deselected { get; private set; } = new();

	private InputHelper _inputHelper = null!;

	public override void Init()
	{
		GDNode.AddChild(_inputHelper = new(), @internal: Node.InternalMode.Back);
		_inputHelper.GodotUnhandledInputEvent += OnUnhandledInput;

		SetProcess(true);
		base.Init();
	}

	public override void PreDelete()
	{
		_inputHelper.GodotUnhandledInputEvent -= OnUnhandledInput;
		_inputHelper.QueueFree();
		_inputHelper.Dispose();
		base.PreDelete();
	}

	private void OnUnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			CancelPickInstance();
		}
	}

	[ScriptMethod]
	public void Select(Instance instance)
	{
		if (SelectedInstances.Contains(instance))
		{
			return;
		}

		SelectedInstances.Add(instance);
		Explorer.Select(instance);
		instance.CreatorSelected();

		if (instance is Dynamic dyn)
		{
			Root.CreatorContext.Gizmos.Select(dyn);
		}

		if (instance is UIField field)
		{
			Root.Container!.UIGizmos.AddBox(field);
		}

		Selected.Invoke(instance);

		if (_pickTcs != null)
		{
			_pickTcs.TrySetResult(instance);
			_pickTcs = null;
			CreatorService.Interface.StopFollowCursorLabel();
		}
		else
		{
			// Update properties if not picking
			_propertiesDirty = true;
		}
	}

	[ScriptMethod]
	public void SelectChild(Instance instance)
	{
		foreach (Instance child in instance.GetChildren())
		{
			Select(child);
		}
	}

	[ScriptMethod]
	public Instance[] GetSelected()
	{
		return [.. SelectedInstances];
	}

	public override void Process(double delta)
	{
		if (_propertiesDirty)
		{
			_propertiesDirty = false;
			RefreshProperties();
		}
		base.Process(delta);
	}

	public void RefreshProperties()
	{
		if (SelectedInstances.Count == 1)
		{
			Properties.Show(SelectedInstances[0]);
		}
		else if (SelectedInstances.Count > 1)
		{
			Properties.ShowMultiple(SelectedInstances);
		}
		else if (SelectedInstances.Count == 0)
		{
			Properties.ClearRoot(Root);
		}
	}

	[ScriptMethod]
	public void Deselect(Instance instance)
	{
		if (!SelectedInstances.Contains(instance))
		{
			return;
		}

		SelectedInstances.Remove(instance);

		if (instance is Dynamic dyn)
		{
			Root.CreatorContext.Gizmos.Deselect(dyn);
		}

		if (instance is UIField field)
		{
			Root.Container!.UIGizmos.RemoveBox(field);
		}

		Explorer.Deselect(instance);
		instance.CreatorDeselected();
		if (_pickTcs == null)
		{
			// Update if not picking
			_propertiesDirty = true;
		}

		Deselected.Invoke(instance);
	}

	[ScriptMethod]
	public void SelectOnly(Instance instance)
	{
		DeselectAll();
		Select(instance);
	}

	[ScriptMethod]
	public void DeselectAll()
	{
		foreach (Instance item in SelectedInstances.ToList())
		{
			Deselect(item);
		}
		SelectedInstances.Clear();
	}

	public Instance GroupInstances(Instance[] instances, GroupAsEnum asWhat = GroupAsEnum.Model)
	{
		// Find the common parent that's not in the selection
		HashSet<Instance> instanceSet = [.. instances];
		Instance? commonParent = instances[0].Parent;

		// Make sure it's not using a parent that's also being grouped
		while (commonParent != null && instanceSet.Contains(commonParent))
		{
			commonParent = commonParent.Parent;
		}

		if (commonParent == null) throw new System.Exception("Cannot group instances at root level.");

		Instance model = asWhat switch
		{
			GroupAsEnum.Folder => New<Folder>(commonParent),
			GroupAsEnum.RigidBody => New<RigidBody>(commonParent),
			_ => New<Model>(commonParent),
		};

		if (model is Dynamic dyn)
		{
			dyn.SetGlobalTransform(Gizmos.GetCenterPivot(instances));
		}

		foreach (Instance item in instances)
		{
			if (item.GetType().IsDefined(typeof(StaticAttribute), true))
			{
				continue;
			}

			// Check if any ancestor of this item is also in the selection
			bool hasParentInSelection = false;
			Instance? current = item.Parent;
			while (current != null)
			{
				if (instanceSet.Contains(current))
				{
					hasParentInSelection = true;
					break;
				}
				current = current.Parent;
			}

			if (!hasParentInSelection)
			{
				item.Parent = model;
			}
		}

		SelectOnly(model);
		return model;
	}

	public Instance[] UngroupModel(Instance model)
	{
		return UngroupModels([model]);
	}

	public Instance[] UngroupModels(Instance[] models)
	{
		List<Instance> extractedInstances = [];

		foreach (Instance item in models)
		{
			if (item is not IGroup and RigidBody) continue;
			foreach (Instance modelItem in item.GetChildren())
			{
				modelItem.Reparent(item.Parent!);
				extractedInstances.Add(modelItem);
			}

			item.Delete();
		}

		DeselectAll();
		foreach (Instance item in extractedInstances)
		{
			Select(item);
		}
		return [.. extractedInstances];
	}

	public Instance[] DuplicateInstances(Instance[] instances)
	{
		List<Instance> newInstances = [];
		DeselectAll();
		foreach (Instance item in instances)
		{
			NetworkedObject netObj = item.Clone(item.Parent);
			if (netObj is Instance i)
			{
				newInstances.Add(i);
				Select(i);
			}
		}
		return [.. newInstances];
	}

	public static void ToggleLockDynamics(Dynamic[] dyns)
	{
		foreach (Dynamic item in dyns)
		{
			item.Locked = !item.Locked;
		}
	}

	public void GroupSelected(GroupAsEnum asWhat = GroupAsEnum.Model)
	{
		if (SelectedInstances.Count <= 0)
		{
			return;
		}

		Root.CreatorContext.History.GroupInstances([.. SelectedInstances], asWhat);
	}

	public void UngroupSelected()
	{
		if (SelectedInstances.Count <= 0)
		{
			return;
		}

		Root.CreatorContext.History.UngroupInstances([.. SelectedInstances]);
	}

	public void DeleteSelected()
	{
		if (SelectedInstances.Count <= 0)
		{
			return;
		}

		Root.CreatorContext.History.DeleteInstances([.. SelectedInstances]);
	}

	public void DuplicateSelected()
	{
		if (SelectedInstances.Count <= 0)
		{
			return;
		}

		Root.CreatorContext.History.DuplicateInstances([.. SelectedInstances]);
	}

	public void ToggleLockSelected()
	{
		if (SelectedInstances.Count <= 0)
		{
			return;
		}

		List<Dynamic> dyns = [];
		foreach (Instance item in SelectedInstances)
		{
			if (item is Dynamic dyn)
			{
				dyns.Add(dyn);
			}
		}
		Root.CreatorContext.History.ToggleLockedDynamics([.. dyns]);
	}

	[ScriptMethod]
	public bool HasSelected(Instance instance) => SelectedInstances.Contains(instance);

	public Task<Instance> RequestPickInstance()
	{
		CreatorService.Interface.StartFollowCursorLabel("Click to pick an instance, or press Esc to cancel.");
		_pickTcs = new();
		return _pickTcs.Task;
	}

	public void CancelPickInstance()
	{
		_pickTcs?.TrySetException(new TaskCanceledException());
		_pickTcs = null;
		CreatorService.Interface.StopFollowCursorLabel();
	}
}
