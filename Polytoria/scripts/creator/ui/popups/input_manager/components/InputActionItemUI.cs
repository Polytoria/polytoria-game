// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;

namespace Polytoria.Creator.UI.Components;

public partial class InputActionItemUI : Button
{
	[Export] private Label _nameLabel = null!;
	[Export] private TextureRect _iconRect = null!;

	public InputAction TargetAction = null!;

	public override void _Ready()
	{
		UpdateAppearance();
		TargetAction.Renamed += UpdateAppearance;
		base._Ready();
	}

	public override void _ExitTree()
	{
		TargetAction.Renamed -= UpdateAppearance;
		base._ExitTree();
	}

	private void UpdateAppearance()
	{
		_nameLabel.Text = TargetAction.Name;

		string iconName = TargetAction switch
		{
			InputActionAxis => "axis",
			InputActionVector2 => "vector2",
			_ => "button"
		};

		_iconRect.Texture = Globals.LoadUIIcon("input-" + iconName);
	}
}
