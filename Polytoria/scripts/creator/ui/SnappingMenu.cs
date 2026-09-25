// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;

namespace Polytoria.Creator.UI;

public partial class SnappingMenu : Control
{
	[Export] private CheckBox _moveCheck = null!;
	[Export] private SpinBox _moveValue = null!;
	[Export] private CheckBox _rotateCheck = null!;
	[Export] private SpinBox _rotateValue = null!;
	[Export] private CheckBox _rotateAlignCheck = null!;

	public override void _Ready()
	{
		_moveValue.ValueChanged += MoveValueChanged;
		_rotateValue.ValueChanged += RotateValueChanged;

		_moveCheck.Toggled += MoveCheckToggled;
		_rotateCheck.Toggled += RotateCheckToggled;

		_rotateAlignCheck.Toggled += RotateAlignCheckToggled;

		base._Ready();
	}

	private void RotateAlignCheckToggled(bool toggledOn)
	{
		CreatorService.Interface.RotateAlignEnabled = toggledOn;
	}

	private void MoveCheckToggled(bool toggledOn)
	{
		CreatorService.Interface.MoveSnapEnabled = toggledOn;
	}

	private void RotateCheckToggled(bool toggledOn)
	{
		CreatorService.Interface.RotateSnapEnabled = toggledOn;
	}

	private void MoveValueChanged(double value)
	{
		CreatorService.Interface.UserMoveSnapping = (float)value;
	}

	private void RotateValueChanged(double value)
	{
		CreatorService.Interface.UserRotateSnapping = (float)value;
	}
}
