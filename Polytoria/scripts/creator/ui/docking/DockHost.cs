// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Creator.UI.Docking;

/// <summary>
/// A dock zone. Every panel has a ID and may be owned by exactly one host.
/// </summary>
public sealed partial class DockHost : VBoxContainer
{
	private const string InternalNodeMetadata = "dock_internal";

	/// Id used for save/restore and as a drag-and-drop target key.
	[Export] public string HostId = "";

	public IReadOnlyList<DockPanel> Panels => _panels;
	public int ActiveIndex { get; private set; } = -1;
	public DockPanel? ActivePanel => ActiveIndex >= 0 && ActiveIndex < _panels.Count ? _panels[ActiveIndex] : null;

	private readonly List<DockPanel> _panels = [];
	private DockTabBar _tabBar = null!;
	private PanelContainer _body = null!;
	private bool _syncingTabSelection;

	public override void _Ready()
	{
		_tabBar = GetNode<DockTabBar>("Bar/TabBar");
		_body = GetNode<PanelContainer>("Body");

		_tabBar.Host = this;
		_tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowNever;
		_tabBar.DragToRearrangeEnabled = false;
		_tabBar.TabSelected += OnTabSelected;

		foreach (Node child in _body.GetChildren())
		{
			if (IsDockableChild(child) && child is Control control)
				AddPanel(new DockPanel(control.Name, control.Name, control), suppressSave: true);
		}

		DockManager.RegisterHost(this);
	}

	private static bool IsDockableChild(Node child)
	{
		return child is Control && !child.HasMeta(InternalNodeMetadata);
	}

	private void OnTabSelected(long idx)
	{
		if (!_syncingTabSelection)
			ActivateIndex((int)idx);
	}

	public override void _ExitTree() => DockManager.UnregisterHost(this);

	public bool ContainsPanelId(string panelId) => _panels.Any(panel => panel.Id == panelId);

	/// <summary>
	/// Adds a valid panel to this host. The manager rejects conflicting IDs,
	/// and any stale ownership in another host is removed before reparenting.
	/// </summary>
	public bool AddPanel(DockPanel requestedPanel, int index = -1, bool suppressSave = false)
	{
		if (!DockManager.TryRegisterPanel(requestedPanel, out DockPanel panel))
		{
			requestedPanel.Content.GetParent()?.RemoveChild(requestedPanel.Content);
			requestedPanel.Content.QueueFree();
			return false;
		}

		DockManager.RemovePanelFromOtherHosts(panel.Id, this);
		if (ContainsPanelId(panel.Id)) return false;

		if (panel.Content.GetParent() != _body)
		{
			panel.Content.GetParent()?.RemoveChild(panel.Content);
			_body.AddChild(panel.Content);
		}

		if (index < 0 || index > _panels.Count) index = _panels.Count;
		_panels.Insert(index, panel);

		_tabBar.AddTab(panel.Title, panel.Icon);
		int addedAt = _tabBar.TabCount - 1;
		if (addedAt != index) _tabBar.MoveTab(addedAt, index);

		ActivateIndex(index);
		if (!suppressSave) DockManager.SaveLayout();
		return true;
	}

	public DockPanel? RemovePanel(DockPanel panel)
	{
		int idx = _panels.FindIndex(existing => existing.Id == panel.Id);
		if (idx < 0) return null;

		DockPanel canonical = _panels[idx];
		_panels.RemoveAt(idx);
		_tabBar.RemoveTab(idx);
		if (canonical.Content.GetParent() == _body) _body.RemoveChild(canonical.Content);

		ActiveIndex = _panels.Count == 0 ? -1 : Mathf.Clamp(idx, 0, _panels.Count - 1);
		if (ActiveIndex >= 0) ActivateIndex(ActiveIndex);

		return canonical;
	}

	public void Reorder(DockPanel panel, int newIndex, bool suppressSave = false)
	{
		int oldIndex = _panels.FindIndex(existing => existing.Id == panel.Id);
		if (oldIndex < 0) return;

		newIndex = Mathf.Clamp(newIndex, 0, _panels.Count - 1);
		if (oldIndex == newIndex) return;

		DockPanel canonical = _panels[oldIndex];
		_panels.RemoveAt(oldIndex);
		_panels.Insert(newIndex, canonical);
		_tabBar.MoveTab(oldIndex, newIndex);
		ActivateIndex(newIndex);
		if (!suppressSave) DockManager.SaveLayout();
	}

	public void ActivateIndex(int idx)
	{
		if (idx < 0 || idx >= _panels.Count) return;

		ActiveIndex = idx;
		for (int i = 0; i < _panels.Count; i++)
			_panels[i].Content.Visible = i == idx;

		if (_tabBar.CurrentTab == idx) return;

		_syncingTabSelection = true;
		try
		{
			_tabBar.CurrentTab = idx;
		}
		finally
		{
			_syncingTabSelection = false;
		}
	}
}
