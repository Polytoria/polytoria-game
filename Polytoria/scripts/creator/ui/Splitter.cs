// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Layout;

namespace Polytoria.Creator.UI;

public sealed partial class Splitter : HSplitContainer
{
	public static Splitter Singleton { get; private set; } = null!;

	public override void _EnterTree()
	{
		Singleton = this;
	}

	public override void _ExitTree()
	{
		if (Singleton == this)
			Singleton = null!;
	}

	private const string LeftPanelId = "left_dock";
	private const string RightPanelId = "right_dock";
	private const string BottomPanelId = "bottom_dock";
	// Bottom_Dock has no functionality regarding Docking,
	// as the Structure of the Center is different from the left and right

	private CollapsiblePanel? _left;
	private CollapsiblePanel? _right;
	private CollapsiblePanel? _bottom;

	public override void _Ready()
	{
		ResolvePanels();
	}

	private void ResolvePanels()
	{
		_left ??= ResolvePanel(LeftPanelId, "Left");
		_right ??= ResolvePanel(RightPanelId, "Right");
		_bottom ??= ResolvePanel(BottomPanelId, "Center/BottomTabs");
	}

	private CollapsiblePanel? ResolvePanel(string panelId, NodePath fallbackPath)
	{
		return CollapsiblePanel.Registry.TryGetValue(panelId, out CollapsiblePanel? panel) ? panel : GetNodeOrNull<CollapsiblePanel>(fallbackPath);
	}
}
