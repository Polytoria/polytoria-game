// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Linq;

namespace Polytoria.Creator.UI.Docking;

public sealed partial class DockRegion : Control
{
	[Export] public string RegionId = "";

	public bool IsSplit { get; private set; }

	private DockHost _single = null!;
	private DockHost _primary = null!;
	private DockHost _secondary = null!;
	private Control _split = null!;

	public override void _Ready()
	{
		_single = GetNode<DockHost>("Single");
		_split = GetNode<Control>("Split");
		_primary = GetNode<DockHost>("Split/Primary");
		_secondary = GetNode<DockHost>("Split/Secondary");

		DockManager.RegisterRegion(this);
		ApplyPresentation();
	}

	public override void _ExitTree() => DockManager.UnregisterRegion(this);

	public static bool CanReadDockData(Variant data)
	{
		return data.VariantType == Variant.Type.Dictionary
			&& ((Godot.Collections.Dictionary)data).ContainsKey("dock_panel_id")
			&& ((Godot.Collections.Dictionary)data).ContainsKey("source_host_id");
	}

	public void DropDockData(Variant data, Vector2 globalPosition)
	{
		if (!CanReadDockData(data)) return;

		Godot.Collections.Dictionary dict = (Godot.Collections.Dictionary)data;
		string panelId = (string)dict["dock_panel_id"];
		string sourceHostId = (string)dict["source_host_id"];

		if (!IsSplit && IsInLowerHalf(globalPosition))
		{
			bool sourceIsInThisRegion = OwnsHost(sourceHostId);
			SetSplit(
				true,
				suppressSave: true,
				panelIdToSecondary: sourceIsInThisRegion ? panelId : null,
				preserveExistingPanelsInPrimary: !sourceIsInThisRegion);
		}

		DockHost target = ResolveDropHost(globalPosition);
		DockManager.MovePanel(panelId, sourceHostId, target.HostId, target.Panels.Count);
	}

	/// <summary>Returns highlight for the current drop target.</summary>
	public Rect2 GetDropPreviewRect(Vector2 globalPosition)
	{
		if (!IsSplit)
		{
			return IsInLowerHalf(globalPosition) ? new Rect2(0, Size.Y * 0.5f, Size.X, Size.Y * 0.5f) : new Rect2(Vector2.Zero, Size);
		}

		DockHost target = ResolveDropHost(globalPosition);
		Rect2 targetRect = target.GetGlobalRect();
		return new Rect2(targetRect.Position - GetGlobalRect().Position, targetRect.Size);
	}

	public void SetSplit(bool split, bool suppressSave = false, string? panelIdToSecondary = null, bool preserveExistingPanelsInPrimary = false)
	{
		if (IsSplit == split)
		{
			ApplyPresentation();
			if (!suppressSave) DockManager.SaveLayout();
			return;
		}

		if (split)
			SplitSingleStack(panelIdToSecondary, preserveExistingPanelsInPrimary);
		else
			MergeSplitStacks();

		IsSplit = split;
		ApplyPresentation();

		if (!suppressSave) DockManager.SaveLayout();
	}

	/// Returns true only when a split region was
	/// collapsed because its upper or lower zone has no remaining panel.
	public bool MergeIfEitherSplitZoneIsEmpty()
	{
		if (!IsSplit || (_primary.Panels.Count > 0 && _secondary.Panels.Count > 0))
			return false;

		MergeSplitStacks();
		IsSplit = false;
		ApplyPresentation();
		return true;
	}

	private bool IsInLowerHalf(Vector2 globalPosition)
	{
		Rect2 bounds = GetGlobalRect();
		return bounds.HasPoint(globalPosition) && globalPosition.Y >= bounds.GetCenter().Y;
	}

	private bool OwnsHost(string hostId)
	{
		return hostId == _single.HostId
			|| hostId == _primary.HostId
			|| hostId == _secondary.HostId;
	}

	private DockHost ResolveDropHost(Vector2 globalPosition)
	{
		if (!IsSplit)
			return _single;

		if (_secondary.GetGlobalRect().HasPoint(globalPosition))
			return _secondary;
		if (_primary.GetGlobalRect().HasPoint(globalPosition))
			return _primary;

		return globalPosition.Y >= GetGlobalRect().GetCenter().Y ? _secondary : _primary;
	}

	private void SplitSingleStack(string? panelIdToSecondary, bool preserveExistingPanelsInPrimary)
	{
		if (_single.Panels.Count == 0) return;

		if (preserveExistingPanelsInPrimary)
		{
			foreach (DockPanel panel in _single.Panels.ToList())
			{
				_single.RemovePanel(panel);
				_primary.AddPanel(panel, suppressSave: true);
			}
			return;
		}

		DockPanel? requestedLowerPanel = _single.Panels.FirstOrDefault(p => p.Id == panelIdToSecondary);

		bool splitRemainingToSecondary = requestedLowerPanel == null;
		DockPanel keepInPrimary = requestedLowerPanel ?? _single.ActivePanel ?? _single.Panels[0];

		foreach (DockPanel panel in _single.Panels.ToList())
		{
			_single.RemovePanel(panel);

			if (splitRemainingToSecondary)
			{
				if (panel == keepInPrimary)
					_primary.AddPanel(panel, suppressSave: true);
				else
					_secondary.AddPanel(panel, suppressSave: true);
			}
			else
			{
				if (panel == requestedLowerPanel)
					_secondary.AddPanel(panel, suppressSave: true);
				else
					_primary.AddPanel(panel, suppressSave: true);
			}
		}
	}

	private void MergeSplitStacks()
	{
		DockPanel? preferred = _primary.ActivePanel ?? _secondary.ActivePanel;
		MoveAll(_primary, _single);
		MoveAll(_secondary, _single);
		ActivatePanelIfPresent(_single, preferred);
	}

	private static void ActivatePanelIfPresent(DockHost host, DockPanel? panel)
	{
		if (panel == null) return;
		for (int i = 0; i < host.Panels.Count; i++)
		{
			if (host.Panels[i] != panel) continue;
			host.ActivateIndex(i);
			return;
		}
	}

	private static void MoveAll(DockHost from, DockHost to)
	{
		foreach (DockPanel panel in from.Panels.ToList())
		{
			from.RemovePanel(panel);
			to.AddPanel(panel, suppressSave: true);
		}
	}

	private void ApplyPresentation()
	{
		_single.Visible = !IsSplit;
		_split.Visible = IsSplit;
	}
}
