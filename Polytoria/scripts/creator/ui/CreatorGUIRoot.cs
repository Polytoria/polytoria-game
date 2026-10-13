// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Docking;

namespace Polytoria.Creator.UI;

public sealed partial class CreatorGUIRoot : Control
{
	public static CreatorGUIRoot Singleton { get; private set; } = null!;
	public CreatorGUIRoot()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		// Prevent the layout of being applied before all Docks are Initilized
		CallDeferred(nameof(ApplySavedDockLayout));
	}

	private static void ApplySavedDockLayout() => DockManager.RestoreLayout();
}
