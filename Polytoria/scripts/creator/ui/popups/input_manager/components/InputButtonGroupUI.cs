// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;
using System.Reflection;

namespace Polytoria.Creator.UI.Components;

public partial class InputButtonGroupUI : FoldableContainer
{
	private const string ButtonItemPath = "res://scenes/creator/popups/input_manager/components/button_item.tscn";
	[Export] private Button _addNewBtn = null!;
	[Export] private Control _listContainer = null!;
	public InputAction TargetAction = null!;
	public string PropName = "";
	public PropertyInfo Property = null!;

	public override void _Ready()
	{
		Title = PropName;
		_addNewBtn.Pressed += PromptAddNew;
		Refresh();
	}

	private void PromptAddNew()
	{
		CreatorService.Interface.PromptBindKey(t =>
		{
			InputButtonCollection val = GetButtons();
			val.AddButton(new() { KeyCode = t.Key, KeyType = t.Type });
			Refresh();
		});
	}

	private InputButtonCollection GetButtons()
	{
		return (InputButtonCollection)Property.GetValue(TargetAction)!;
	}

	public void RemoveButton(InputButton button)
	{
		GetButtons().RemoveButton(button);
		Refresh();
	}

	private void Refresh()
	{
		Clear();
		ListButtons();
	}

	private void Clear()
	{
		foreach (Node item in _listContainer.GetChildren())
		{
			item.QueueFree();
		}
	}

	private void ListButtons()
	{
		InputButtonCollection val = GetButtons();
		foreach (InputButton btn in val)
		{
			InputButtonItemUI item = Globals.CreateInstanceFromScene<InputButtonItemUI>(ButtonItemPath);
			item.TargetAction = TargetAction;
			item.TargetButton = btn;
			item.GroupParent = this;
			_listContainer.AddChild(item);
		}
	}
}
