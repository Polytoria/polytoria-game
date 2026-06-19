// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public partial class UIInventory : Control
{
	public const int MaximumToolSlot = 6;
	private Inventory _inventory = null!;
	private Player _localplr = null!;
	private Control _layout = null!;
	private readonly Dictionary<Tool, UIToolItem> _tools = [];
	private readonly List<Tool?> _toolSlot = [];
	private readonly List<Tool?> _backpackSlot = [];
	private UIToolAddItem _addSlotItemBtn = null!;

	[Export] private Control _backpackFlowContainer = null!;
	[Export] private Control _backpackNoneView = null!;
	[Export] private AnimationPlayer _backpackAnim = null!;

	public CoreUIRoot CoreUI = null!;
	public bool IsBackpackOpened = false;
	public int CurrentSlotIndex = -1;

	public override void _Ready()
	{
		_localplr = World.Current!.Players.LocalPlayer;
		_inventory = _localplr.Inventory;
		_layout = GetNode<Control>("Layout");
		_inventory.ChildAdded.Connect(OnChildEnterInventory);
		_inventory.ChildRemoved.Connect(OnChildExitInventory);
		_localplr.ChildAdded.Connect(OnChildEnterInventory);
		_localplr.ChildRemoved.Connect(OnChildExitInventory);

		PackedScene packed = GD.Load<PackedScene>("res://scenes/client/ui/inventory/slot_add_item.tscn");
		_addSlotItemBtn = packed.Instantiate<UIToolAddItem>();
		_addSlotItemBtn.Root = this;
		_layout.AddChild(_addSlotItemBtn, false, InternalMode.Back);
		_addSlotItemBtn.Visible = false;

		foreach (Instance item in _inventory.GetChildren())
		{
			OnChildEnterInventory(item);
		}
	}

	public void ToggleBackpack()
	{
		if (!CoreUI.Service.UseBackpack) return;
		if (IsBackpackOpened)
		{
			CloseBackpack();
		}
		else
		{
			OpenBackpack();
		}
	}

	public void OpenBackpack()
	{
		if (!CoreUI.Service.UseBackpack) return;
		IsBackpackOpened = true;
		_backpackAnim.Stop();
		_backpackAnim.Play("appear");
	}

	public void CloseBackpack()
	{
		if (!CoreUI.Service.UseBackpack) return;
		IsBackpackOpened = false;
		_backpackAnim.Stop();
		_backpackAnim.Play("disappear");
	}

	private void OnChildEnterInventory(Instance child)
	{
		if (child is Tool tool)
		{
			AddTool(tool);
		}
	}

	private void OnChildExitInventory(Instance child)
	{
		if (child is Tool tool)
		{
			RemoveTool(tool);
		}
	}

	private void AddTool(Tool tool)
	{
		if (_tools.ContainsKey(tool)) { return; }

		AddNewToolInSlot(tool);
	}

	internal void StartDragFrom(UIToolItem item)
	{
		if (item.IsInBackpack && _toolSlot.Count < MaximumToolSlot)
		{
			_addSlotItemBtn.Visible = true;
		}
	}

	public override async void _Input(InputEvent @event)
	{
		if (!Visible) return;
		if (@event.IsEcho()) return;
		if (_addSlotItemBtn.Visible && @event is InputEventMouseButton btn)
		{
			if (btn.IsReleased() && btn.ButtonIndex == MouseButton.Left)
			{
				// Wait for frame to let add slot recieve any item drop
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				_addSlotItemBtn.Visible = false;
			}
		}
		base._Input(@event);
		if (!CoreUIRoot.Singleton.Root.Input.IsGameFocused) return;
		if (@event.IsActionPressed("equip_tool_cycle_left"))
		{
			int i = CurrentSlotIndex - 1;
			if (CurrentSlotIndex == -1)
			{
				i = _toolSlot.Count - 1;
			}
			EquipSlot(i);
		}
		else if (@event.IsActionPressed("equip_tool_cycle_right"))
		{
			EquipSlot(CurrentSlotIndex + 1);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible) return;
		if (@event.IsEcho()) return;
		if (@event.IsActionPressed("toggle_backpack"))
		{
			ToggleBackpack();
		}
		base._UnhandledInput(@event);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible) return;
		if (@event.IsEcho()) return;
		if (@event is InputEventKey key)
		{
			if (!key.Pressed) { return; }
			string keyAsText = OS.GetKeycodeString(key.Keycode);
			if (int.TryParse(keyAsText, out int value))
			{
				int index = value - 1;
				EquipSlot(index);
			}
		}
		base._UnhandledKeyInput(@event);
	}

	private int? GetAvailableSlot()
	{
		for (int i = 0; i < _toolSlot.Count; i++)
		{
			if (_toolSlot[i] == null)
			{
				return i;
			}
		}
		return null;
	}

	private int? GetAvailableBackpackSlot()
	{
		for (int i = 0; i < _toolSlot.Count; i++)
		{
			if (_toolSlot[i] == null)
			{
				return i;
			}
		}
		return null;
	}

	public UIToolItem? GetToolItemFromTool(Tool tool)
	{
		if (_tools.TryGetValue(tool, out UIToolItem? UIToolItem))
		{
			return UIToolItem;
		}
		return null;
	}

	private UIToolItem CreateUIToolItem(Tool tool)
	{
		PackedScene packed = GD.Load<PackedScene>("res://scenes/client/ui/inventory/tool_item.tscn");
		UIToolItem item = packed.Instantiate<UIToolItem>();
		item.LinkedTool = tool;
		item.Root = this;
		item.Player = _localplr;
		return item;
	}

	public void PutToolInBackpack(Tool tool, int slot)
	{
		if (tool.Holder == _localplr)
		{
			// Unequip tool if holding right now
			_localplr.UnequipTool();
		}
		UIToolItem? item = GetToolItemFromTool(tool);
		if (item != null)
		{
			item.GetParent().RemoveChild(item);
		}
		else
		{
			item = CreateUIToolItem(tool);
		}

		_backpackFlowContainer.AddChild(item);
		_toolSlot.Remove(tool);
		_tools.TryAdd(tool, item);
		_backpackSlot.Insert(slot, tool);
		item.IsInBackpack = true;

		UpdateSlots();
	}

	public void AddNewToolInBackpack(Tool tool)
	{
		int? slot = GetAvailableBackpackSlot();
		slot ??= _backpackSlot.Count;
		PutToolInBackpack(tool, (int)slot);
	}

	public void AddNewToolInSlot(Tool tool)
	{
		int? slot = GetAvailableSlot();
		slot ??= _toolSlot.Count;
		PutToolInSlot(tool, (int)slot);
	}

	public void PutToolInSlot(Tool tool, int slot)
	{
		_toolSlot.Remove(tool);
		_backpackSlot.Remove(tool);

		_toolSlot.Insert(slot, tool);

		UIToolItem? item = GetToolItemFromTool(tool);
		if (item != null)
		{
			item.GetParent()?.RemoveChild(item);
		}
		else
		{
			item = CreateUIToolItem(tool);
			_tools[tool] = item;
		}

		item.IsInBackpack = false;
		item.Root = this;

		_layout.AddChild(item);
		item.ToolIndex = slot;

		if (_toolSlot.Count > MaximumToolSlot)
		{
			AddNewToolInBackpack(tool);
		}
		else
		{
			UpdateSlots();
		}

		if (tool.Holder == _localplr)
		{
			// Set active if holding
			item.SetPressedNoSignal(true);
		}
	}

	public Tool? GetToolFromNetworkID(string netID)
	{
		foreach (Tool tool in _tools.Keys)
		{
			if (tool.NetworkedObjectID == netID)
			{
				return tool;
			}
		}
		return null;
	}

	public void MoveToolSlot(UIToolItem fromItem, UIToolItem toItem)
	{
		int toIndex = toItem.ToolIndex;

		if (fromItem.IsInBackpack)
		{
			_backpackSlot.Remove(fromItem.LinkedTool);
		}
		else
		{
			_toolSlot.Remove(fromItem.LinkedTool);
		}

		if (toItem.IsInBackpack)
		{
			PutToolInBackpack(fromItem.LinkedTool, toIndex);
		}
		else
		{
			PutToolInSlot(fromItem.LinkedTool, toIndex);
		}

		// Update UI indexes for all tools
		UpdateSlots();
	}

	private void UpdateSlots()
	{
		for (int i = 0; i < _toolSlot.Count; i++)
		{
			Tool? tool = _toolSlot[i];
			if (tool != null && _tools.TryGetValue(tool, out UIToolItem? uiTool))
			{
				uiTool.ToolIndex = i;
				_layout.MoveChild(uiTool, i);
			}
		}

		for (int i = 0; i < _backpackSlot.Count; i++)
		{
			Tool? tool = _backpackSlot[i];
			if (tool != null && _tools.TryGetValue(tool, out UIToolItem? uiTool))
			{
				if (uiTool.IsInBackpack)
				{
					uiTool.ToolIndex = i;
					_backpackFlowContainer.MoveChild(uiTool, i);
				}
			}
		}

		_backpackNoneView.Visible = _backpackSlot.Count == 0;
	}

	private async void RemoveTool(Tool tool)
	{
		// Wait for tool to enter another tree first
		if (!tool.IsDeleted)
			await tool.TreeEntered.Wait();

		// If the tool was reparented back to the player or their inventory, keep it
		if (!tool.IsDeleted && (tool.Parent == _localplr || tool.Parent == _localplr.Inventory)) return;

		if (_tools.TryGetValue(tool, out UIToolItem? toolItem))
		{
			_tools.Remove(tool);
			int slot = _toolSlot.FindIndex(item => item == tool);
			if (slot != -1)
			{
				_toolSlot.RemoveAt(slot);
			}
			toolItem.QueueFree();
			UpdateSlots();
		}
	}

	private void EquipSlot(int index)
	{
		Tool? oldTool = _localplr.HoldingTool;
		if (_localplr.HoldingTool != null)
		{
			CurrentSlotIndex = -1;
			_localplr.UnequipTool();
		}
		if (index < 0 || index >= _toolSlot.Count) { return; }
		if (_toolSlot[index] != null)
		{
			Tool tool = _toolSlot[index]!;
			if (oldTool == tool) return;
			CurrentSlotIndex = index;
			_localplr.EquipTool(tool);
		}
	}
}
