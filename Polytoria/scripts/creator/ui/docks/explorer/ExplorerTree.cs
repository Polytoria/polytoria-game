// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.UI;

public partial class ExplorerTree : Tree
{
	public ExplorerItemContextMenu? ItemContextMenu;
	public World Root = null!;
	public readonly Dictionary<Instance, TreeItem> InstanceToItem = [];
	public readonly Dictionary<TreeItem, Instance> ItemToInstance = [];
	public TreeItem? ScrollToTarget = null!;

	public override void _Ready()
	{
		ItemActivated += OnItemActivated;
		base._Ready();
	}

	public override void _Process(double delta)
	{
		if (ScrollToTarget != null)
		{
			if (GodotObject.IsInstanceValid(ScrollToTarget))
			{
				ScrollToItem(ScrollToTarget);
			}
			ScrollToTarget = null;
		}
		base._Process(delta);
	}

	/// <summary>
	/// Basically scroll to item but wait for next frame
	/// </summary>
	/// <param name="target"></param>
	public void ScrollToItemFrame(TreeItem target)
	{
		ScrollToTarget = target;
	}

	public override async void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
			{
				TreeItem clickedItem = GetItemAtPosition(mouseEvent.Position);
				if (clickedItem != null)
				{
					ItemContextMenu?.Close();

					// This is needed because selected instances couldn't update beforehand (especially with RMB select)
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

					List<Instance> instances = World.Current!.CreatorContext.Selections.SelectedInstances;

					if (instances.Count == 1)
					{
						instances.Clear();
						instances.Add(Explorer.GetInstanceFromTreeItem(clickedItem)!);
					}

					ItemContextMenu = new() { Targets = instances };
					AddChild(ItemContextMenu);
					ItemContextMenu.PopupAtCursor();
				}
			}
		}
		else if (@event.IsActionPressed("rename"))
		{
			EditSelected();
		}
		base._GuiInput(@event);
	}

	private void OnItemActivated()
	{
		TreeItem target = GetSelected();

		if (target == null)
		{
			return;
		}

		Instance clickedInstance = ItemToInstance[target];

		if (clickedInstance != null && clickedInstance is Datamodel.Script script)
		{
			CreatorService.OpenScript(script);
		}
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		return new InstanceDragData()
		{
			Instances = [.. ItemToInstance
			.Where(kvp => IsInstanceValid(kvp.Key) && kvp.Key.IsSelected(0))
			.Select(kvp => kvp.Value)]
		}.Serialize();
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		DropModeFlags = (int)(DropModeFlagsEnum.OnItem | DropModeFlagsEnum.Inbetween);

		return true;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		CreatorHistory history = Root.CreatorContext.History;
		TreeItem targetItem = GetItemAtPosition(atPosition);
		int dropSection = GetDropSectionAtPosition(atPosition);

		Instance target = ItemToInstance[targetItem];

		DropModeFlags = (int)DropModeFlagsEnum.Disabled;

		if (target == null)
			return;

		IDragDataUnion? dragData = DragData.Deserialize(data);

		if (dragData == null) return;

		List<TreeItem> draggedItems = [];

		if (dragData is InstanceDragData instanceDrag)
		{
			foreach (Instance item in instanceDrag.Instances)
			{
				draggedItems.Add(InstanceToItem[item]);
			}
		}
		else if (dragData is FileDragData fileDrag)
		{
			if (fileDrag.Files.Length == 1)
			{
				string file = fileDrag.Files[0];
				string fileExt = file.GetExtension();

				if (Globals.ScriptFileExtensions.Contains(fileExt))
				{
					bool createAsChild = true;
					string n = CreatorService.GetScriptNameFromPath(file);
					ScriptTypeEnum st = CreatorService.GetScriptTypeFromPath(file);

					if (createAsChild)
					{
						Script s;

						if (st == ScriptTypeEnum.Server)
						{
							s = Root.New<ServerScript>();
						}
						else if (st == ScriptTypeEnum.Client)
						{
							s = Root.New<ClientScript>();
						}
						else
						{
							s = Root.New<ModuleScript>();
						}

						s.LinkedScript = Root.Assets.GetFileLinkByPath(file);
						s.Name = n.ToPascalCase();

						s.Parent = target;
						Root.CreatorContext.Selections.DeselectAll();
						Root.CreatorContext.Selections.Select(s);
					}
				}
				else if (fileExt == Globals.ModelFileExtension)
				{
					_ = Root.LinkedSession.InsertModel(file, target);
				}

				Root.PlayerGUI.GrabFocus();
			}
		}
		else
		{
			return;
		}

		Instance? parentTo = null;
		int insertIndex = 0;

		switch (dropSection)
		{
			case -1: // Above Item
				parentTo = target.Parent;
				insertIndex = target.Index;
				break;
			case 0: // On Item
				parentTo = target;
				insertIndex = parentTo.GetChildren().Length; // Add at end
				break;
			case 1: // Below Item
				parentTo = target.Parent;
				insertIndex = target.Index + 1;

				// Check if target is the descendant of any dragged items
				bool isTargetParent = draggedItems
					.Select(item => ItemToInstance[item])
					.Where(inst => inst != null)
					.Any(inst => inst.Parent == target || inst.IsDescendantOf(target));

				if (isTargetParent)
				{
					// Moving to top of parent
					parentTo = target;
					insertIndex = 0;
				}
				break;
		}

		List<Instance> sortedDraggedInstances = [.. draggedItems
		.Select(item => ItemToInstance[item])
		.Where(inst => inst != null)
		.OrderBy(inst => inst.Index)];

		List<(Instance instance, Instance? oldParent, int oldIndex)> originalState = [];
		List<(Instance instance, Instance? newParent, int newIndex)> finalState = [];

		foreach (Instance draggedInstance in sortedDraggedInstances)
		{
			if (parentTo != null)
			{
				if (draggedInstance.IsAncestorOf(parentTo) || draggedInstance == parentTo)
					continue;

				try
				{
					Instance? oldParent = draggedInstance.Parent;
					int oldIndex = draggedInstance.Index;
					originalState.Add((draggedInstance, oldParent, oldIndex));

					// Calculate adjustment if moving within same parent
					int adjustedIndex = insertIndex;
					if (draggedInstance.Parent == parentTo && draggedInstance.Index < insertIndex)
					{
						// Item is being removed from before the target position
						adjustedIndex--;
					}

					finalState.Add((draggedInstance, parentTo, adjustedIndex));
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					CreatorService.Interface.PopupAlert(ex.Message);
					return;
				}
			}
		}

		// Add history action
		if (originalState.Count > 0)
		{
			history.NewAction($"Move {originalState.Count} instance(s)");

			history.AddDoCallback(new((_) =>
			{
				Root.CreatorContext.Selections.DeselectAll();
				foreach (var (instance, newParent, newIndex) in finalState)
				{
					if (newParent != null)
					{
						if (instance.Parent != newParent)
						{
							instance.Parent = newParent;
						}
						newParent.MoveChild(instance, newIndex);
						Root.CreatorContext.Selections.Select(instance);
					}
				}
			}));

			history.AddUndoCallback(new((_) =>
			{
				Root.CreatorContext.Selections.DeselectAll();

				for (int i = originalState.Count - 1; i >= 0; i--)
				{
					var (instance, oldParent, oldIndex) = originalState[i];
					if (oldParent != null)
					{
						instance.Parent = oldParent;
						oldParent.MoveChild(instance, oldIndex);
						Root.CreatorContext.Selections.Select(instance);
					}
				}
			}));

			history.CommitAction();
		}

	}

}
