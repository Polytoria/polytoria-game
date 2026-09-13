// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.Docking;

public sealed partial class DockTabBar : TabBar
{
	public DockHost Host = null!;

	public override Variant _GetDragData(Vector2 atPosition)
	{
		int idx = GetTabIdxAtPoint(atPosition);
		if (idx < 0 || idx >= Host.Panels.Count) return default;

		Label preview = new()
		{
			Text = GetTabTitle(idx),
			Modulate = new Color(1f, 1f, 1f, 0.85f)
		};
		SetDragPreview(preview);
		DockManager.BeginTabDrag();

		return new Godot.Collections.Dictionary
		{
			["dock_panel_id"] = Host.Panels[idx].Id,
			["source_host_id"] = Host.HostId
		};
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		return data.VariantType == Variant.Type.Dictionary
			   && ((Godot.Collections.Dictionary)data).ContainsKey("dock_panel_id");
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		Godot.Collections.Dictionary dict = (Godot.Collections.Dictionary)data;
		string panelId = (string)dict["dock_panel_id"];
		string sourceHostId = (string)dict["source_host_id"];

		int dropIndex = GetTabIdxAtPoint(atPosition);
		if (dropIndex < 0) dropIndex = TabCount;

		DockManager.MovePanel(panelId, sourceHostId, Host.HostId, dropIndex);
	}
}
