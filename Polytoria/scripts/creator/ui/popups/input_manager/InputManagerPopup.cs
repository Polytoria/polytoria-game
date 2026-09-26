// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Components;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class InputManagerPopup : PopupWindowBase
{
	private const string ActionItemPath = "res://scenes/creator/popups/input_manager/components/action_item.tscn";
	private const string ActionViewPath = "res://scenes/creator/popups/input_manager/components/action_view.tscn";

	[Export] private LineEdit _searchEdit = null!;
	[Export] private MenuButton _addButton = null!;
	[Export] private Control _viewContainer = null!;
	[Export] private Control _inputList = null!;

	private CreatorSession _session = null!;
	private ButtonGroup _btnGroup = new();
	private Control? _currentView;

	public override void _Ready()
	{
		_session = CreatorService.CurrentSession!;
		PopupMenu menu = _addButton.GetPopup();
		menu.IdPressed += OnAddIdPressed;
		RefreshActions();
		_btnGroup.Pressed += OnBtnGroupPressed;
		base._Ready();
	}

	private void OnBtnGroupPressed(BaseButton button)
	{
		if (button is InputActionItemUI item)
		{
			ShowInputView(item.TargetAction);
		}
	}

	private void ShowInputView(InputAction action)
	{
		ClearView();
		InputActionViewUI view = Globals.CreateInstanceFromScene<InputActionViewUI>(ActionViewPath);
		view.TargetAction = action;
		view.Manager = this;

		_currentView = view;
		_viewContainer.AddChild(view);
	}

	public void DeleteAction(InputAction action)
	{
		ClearView();
		_session.InputMap.Actions.Remove(action);
		RefreshActions();
	}

	private void ClearView()
	{
		if (IsInstanceValid(_currentView))
		{
			_currentView.QueueFree();
			_currentView = null;
		}
	}

	private void OnAddIdPressed(long idx)
	{
		CreatorService.Interface.PromptGiveName("Input name...", str =>
		{
			if (string.IsNullOrWhiteSpace(str)) return;

			PT.Print("Add new: ", str);

			InputAction newz = idx switch
			{
				1 => new InputActionAxis() { Name = str },
				2 => new InputActionVector2() { Name = str },
				_ => new InputActionButton() { Name = str }
			};

			_session.InputMap.Actions.Add(newz);

			RefreshActions();
			ShowInputView(newz);
		}, "Give Action name...");
	}

	private void RefreshActions()
	{
		Clear();
		ListItems();
	}

	private void Clear()
	{
		foreach (Node item in _inputList.GetChildren())
		{
			item.QueueFree();
		}
	}

	private void ListItems()
	{
		foreach (InputAction action in _session.InputMap.Actions)
		{
			InputActionItemUI item = Globals.CreateInstanceFromScene<InputActionItemUI>(ActionItemPath);
			item.TargetAction = action;
			item.ButtonGroup = _btnGroup;

			_inputList.AddChild(item);
		}
	}
}
