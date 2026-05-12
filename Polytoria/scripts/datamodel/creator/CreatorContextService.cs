// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Creator;
using Polytoria.Shared;

namespace Polytoria.Datamodel.Creator;

[Static("CreatorContext")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class CreatorContextService : Instance
{
	internal Camera Freelook = null!;
	internal Gizmos Gizmos = null!;

	public bool IsViewportFocused
	{
		get
		{
			if (!Globals.Singleton.GetWindow().HasFocus()) return false;
			Control? rootFocusOwner = GDNode.GetWindow().GuiGetFocusOwner();
			Control? focusOwner = GDNode.GetViewport().GuiGetFocusOwner();
			return rootFocusOwner == Root.Container || focusOwner is InputFallbackBase;
		}
	}

	public CreatorSelections Selections = null!;
	public CreatorHistory History = null!;
	public CreatorAddons Addons = null!;
	public CreatorGUI GUIOverlay = null!;

	public override void Init()
	{
		if (Root.Container == null) return;
		NameOverride = "CreatorContext";

		Freelook = new()
		{
			Name = "FreeLook",
			Root = Root,
			Parent = this,
			Mode = Camera.CameraModeEnum.Free
		};
		Freelook.GDNode3D.GlobalPosition = new(0, 6, -4);
		Freelook.GDNode3D.RotationDegrees = new(-25, 0, 0);

		Gizmos = new() { Name = "Gizmos" };
		Gizmos.Attach(Root);
		GDNode.AddChild(Gizmos, false, Node.InternalMode.Front);

		GUIOverlay = Globals.LoadInstance<CreatorGUI>(Root);
		GUIOverlay.NetworkParent = this;

		Selections = Globals.LoadInstance<CreatorSelections>(Root);
		Selections.NameOverride = "Selections";
		Selections.NetworkParent = this;

		History = Globals.LoadInstance<CreatorHistory>(Root);
		History.NameOverride = "History";
		History.NetworkParent = this;

		Addons = Globals.LoadInstance<CreatorAddons>(Root);
		Addons.NameOverride = "Addons";
		Addons.NetworkParent = this;

		base.Init();
	}
}
