// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.UI.Layout;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Creator.UI.Docking;

/// <summary>
/// Manages all the panels, their ownership and the layout state, as well as the drag surface
/// </summary>
public static class DockManager
{
	private static readonly Dictionary<string, DockHost> _hosts = [];
	private static readonly Dictionary<string, DockRegion> _regions = [];
	private static readonly Dictionary<string, DockPanel> _panelsById = [];
	private static DockDragSurface? _dragSurface;

	public static void RegisterHost(DockHost host) => _hosts[host.HostId] = host;

	public static void UnregisterHost(DockHost host)
	{
		_hosts.Remove(host.HostId);
		if (_hosts.Count == 0)
			_panelsById.Clear();
	}
	public static void RegisterRegion(DockRegion region) => _regions[region.RegionId] = region;
	public static void UnregisterRegion(DockRegion region) => _regions.Remove(region.RegionId);
	public static void RegisterDragSurface(DockDragSurface surface) => _dragSurface = surface;

	public static void UnregisterDragSurface(DockDragSurface surface)
	{
		if (_dragSurface == surface) _dragSurface = null;
	}

	/// <summary>
	/// Registers a DockPanel by its ID, returning the registered
	/// valid instance and returning true only if the ID is new or
	/// belongs to an existing panel with the exact same content.
	/// </summary>
	public static bool TryRegisterPanel(DockPanel candidate, out DockPanel valid)
	{
		if (string.IsNullOrWhiteSpace(candidate.Id))
		{
			valid = null!;
			return false;
		}

		if (_panelsById.TryGetValue(candidate.Id, out DockPanel? existing))
		{
			valid = existing;
			return existing.Content == candidate.Content;
		}

		_panelsById[candidate.Id] = candidate;
		valid = candidate;
		return true;
	}

	public static void BeginTabDrag() => _dragSurface?.BeginDrag();

	public static DockRegion? FindVisibleRegionAt(Godot.Vector2 globalPosition)
	{
		return _regions.Values.FirstOrDefault(region => region.IsVisibleInTree() && region.GetGlobalRect().HasPoint(globalPosition));
	}

	public static void RemovePanelFromOtherHosts(string panelId, DockHost keep)
	{
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel)) return;
		foreach (var host in _hosts.Values.ToList().Where(host => host != keep && host.ContainsPanelId(panelId)))
		{
			host.RemovePanel(panel);
		}
	}

	public static bool MovePanel(string panelId, string fromHostId, string toHostId, int index)
	{
		if (!_hosts.TryGetValue(fromHostId, out DockHost? requestedFrom)) return false;
		if (!_hosts.TryGetValue(toHostId, out DockHost? to)) return false;
		if (!_panelsById.TryGetValue(panelId, out DockPanel? panel)) return false;

		DockHost? from = _hosts.Values.FirstOrDefault(host => host.ContainsPanelId(panelId)) ?? requestedFrom;
		if (!from.ContainsPanelId(panelId)) return false;

		// Remove any old registration before the transfer is applied.
		RemovePanelFromOtherHosts(panelId, from);

		if (from == to)
		{
			from.Reorder(panel, index, suppressSave: true);
		}
		else
		{
			DockPanel? removed = from.RemovePanel(panel);
			if (removed == null) return false;

			if (!to.AddPanel(removed, index, suppressSave: true))
			{
				from.AddPanel(removed, suppressSave: true);
				return false;
			}
		}

		MergeEmptyRegions();
		SaveLayout();
		return true;
	}

	public static void SaveLayout()
	{
		DockLayoutData data = new();
		foreach ((string id, DockHost host) in _hosts)
		{
			data.Zones[id] = new DockZoneData
			{
				PanelIds = [.. host.Panels.Select(panel => panel.Id).Distinct()],
				ActiveIndex = host.ActiveIndex
			};
		}

		foreach ((string id, DockRegion region) in _regions)
			data.RegionSplitModes[id] = region.IsSplit;

		DockLayoutService.SaveDockLayout(data);
	}

	public static void RestoreLayout()
	{
		DockLayoutData? data = DockLayoutService.LoadDockLayout();
		if (data == null) return;

		foreach ((string regionId, bool isSplit) in data.RegionSplitModes)
		{
			if (_regions.TryGetValue(regionId, out DockRegion? region))
				region.SetSplit(isSplit, suppressSave: true);
		}

		foreach ((string hostId, DockZoneData zone) in data.Zones)
		{
			if (!_hosts.TryGetValue(hostId, out DockHost? host)) continue;

			int order = 0;
			foreach (string panelId in zone.PanelIds.Distinct())
			{
				if (!_panelsById.TryGetValue(panelId, out DockPanel? panel)) continue;

				DockHost? currentHost = _hosts.Values
					.FirstOrDefault(candidate => candidate.ContainsPanelId(panelId));

				if (currentHost != host)
				{
					currentHost?.RemovePanel(panel);
					host.AddPanel(panel, order, suppressSave: true);
				}
				else
				{
					host.Reorder(panel, order, suppressSave: true);
				}

				order++;
			}

			if (zone.ActiveIndex >= 0 && zone.ActiveIndex < host.Panels.Count)
				host.ActivateIndex(zone.ActiveIndex);
		}

		MergeEmptyRegions();
	}

	private static void MergeEmptyRegions()
	{
		foreach (DockRegion region in _regions.Values)
			region.MergeIfEitherSplitZoneIsEmpty();
	}

	// TODO: Add a "Reset Layout" button to the UI under View.
	public static void ResetLayout() => DockLayoutService.ResetToDefaults();
}
