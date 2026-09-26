// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Popups;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;
using System.Reflection;

namespace Polytoria.Creator.UI.Components;

public partial class InputActionViewUI : Control
{
	private const string ButtonGroupPath = "res://scenes/creator/popups/input_manager/components/button_group.tscn";

	[Export] private Label _actionTypeLabel = null!;
	[Export] private Label _actionNameLabel = null!;
	[Export] private LineEdit _actionNameEdit = null!;
	[Export] private Button _deleteBtn = null!;

	public InputManagerPopup Manager = null!;
	public InputAction TargetAction = null!;

	public override void _Ready()
	{
		_actionNameLabel.Text = TargetAction.Name;

		string actionType = TargetAction switch
		{
			InputActionButton => "Button Action",
			InputActionAxis => "Axis Action",
			InputActionVector2 => "Vector2 Action",
			_ => "Action",
		};

		_actionTypeLabel.Text = actionType;
		_actionNameLabel.GuiInput += OnActionLabelGuiInput;

		foreach (PropertyInfo prop in TargetAction.GetType().GetProperties())
		{
			if (prop.PropertyType == typeof(InputButtonCollection))
			{
				InputButtonGroupUI group = Globals.CreateInstanceFromScene<InputButtonGroupUI>(ButtonGroupPath);
				group.TargetAction = TargetAction;
				group.PropName = prop.Name;
				group.Property = prop;
				AddChild(group);
			}
		}

		_actionNameEdit.TextSubmitted += _ => OnActionNameChange();
		_actionNameEdit.FocusExited += OnActionNameChange;
		_deleteBtn.Pressed += OnDeletePressed;
	}

	private void OnDeletePressed()
	{
		Manager.DeleteAction(TargetAction);
	}

	private void OnActionNameChange()
	{
		_actionNameEdit.Visible = false;
		string setTo = _actionNameEdit.Text;

		if (string.IsNullOrWhiteSpace(setTo))
		{
			return;
		}

		_actionNameLabel.Text = setTo;
		TargetAction.Name = setTo;
	}

	private void OnActionLabelGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			_actionNameEdit.Visible = true;
			_actionNameEdit.GrabFocus();
		}
	}
}
