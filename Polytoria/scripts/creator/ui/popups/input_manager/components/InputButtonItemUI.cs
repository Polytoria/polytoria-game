// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using Polytoria.Enums;

namespace Polytoria.Creator.UI.Components;

public partial class InputButtonItemUI : Control
{
	[Export] private Label _keyNameLabel = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private Button _removeBtn = null!;

	public InputAction TargetAction = null!;
	public InputButton TargetButton = null!;
	public InputButtonGroupUI GroupParent = null!;

	public override void _Ready()
	{
		string keyModePart = TargetButton.KeyMode switch
		{
			KeyModeEnum.PhysicalKeyCode => " (Physical)",
			_ => "",
		};
		_keyNameLabel.Text = TargetButton.KeyCode.ToString() + keyModePart;
		_removeBtn.Pressed += OnRemovePressed;
	}

	private void OnRemovePressed()
	{
		GroupParent.RemoveButton(TargetButton);
	}
}
