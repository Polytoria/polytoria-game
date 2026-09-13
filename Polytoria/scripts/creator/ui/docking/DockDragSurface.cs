// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI.Docking;

public sealed partial class DockDragSurface : Control
{
	private bool _active;
	private DockRegion? _hoverRegion;
	private Rect2 _previewRect;

	public override void _Ready()
	{
		Visible = false;
		MouseFilter = MouseFilterEnum.Ignore;
		DockManager.RegisterDragSurface(this);
	}

	public override void _ExitTree() => DockManager.UnregisterDragSurface(this);

	public void BeginDrag()
	{
		_active = true;
		Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void EndDrag()
	{
		if (!_active && !Visible) return;
		_active = false;
		_hoverRegion = null;
		_previewRect = default;
		Visible = false;
		MouseFilter = MouseFilterEnum.Ignore;
		QueueRedraw();
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (!_active || !DockRegion.CanReadDockData(data))
		{
			ClearHover();
			return false;
		}

		Vector2 globalPosition = GetGlobalRect().Position + atPosition;
		DockRegion? region = DockManager.FindVisibleRegionAt(globalPosition);
		if (region == null)
		{
			ClearHover();
			return false;
		}

		SetHover(region, globalPosition);
		return true;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		Vector2 globalPosition = GetGlobalRect().Position + atPosition;
		DockRegion? region = DockManager.FindVisibleRegionAt(globalPosition);
		region?.DropDockData(data, globalPosition);
		EndDrag();
	}

	public override void _Process(double delta)
	{
		if (_active && !GetViewport().GuiIsDragging())
			EndDrag();
	}

	public override void _Draw()
	{
		if (_hoverRegion == null || _previewRect.Size.X <= 0 || _previewRect.Size.Y <= 0)
			return;

		Color fill = new(0.18f, 0.60f, 1.0f, 0.22f);
		Color border = new(0.32f, 0.74f, 1.0f, 0.95f);
		DrawRect(_previewRect, fill, filled: true);
		DrawRect(_previewRect, border, filled: false, width: 2.0f);
	}

	private void SetHover(DockRegion region, Vector2 globalPosition)
	{
		Rect2 regionLocalRect = region.GetDropPreviewRect(globalPosition);
		Vector2 globalPreviewOrigin = region.GetGlobalRect().Position + regionLocalRect.Position;
		Rect2 nextPreview = new(globalPreviewOrigin - GetGlobalRect().Position, regionLocalRect.Size);

		if (_hoverRegion == region && _previewRect == nextPreview) return;
		_hoverRegion = region;
		_previewRect = nextPreview;
		QueueRedraw();
	}

	private void ClearHover()
	{
		if (_hoverRegion == null && _previewRect == default) return;
		_hoverRegion = null;
		_previewRect = default;
		QueueRedraw();
	}
}
