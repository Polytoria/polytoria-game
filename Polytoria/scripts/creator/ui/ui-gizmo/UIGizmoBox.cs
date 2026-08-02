// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.UI.Gizmos;

public partial class UIGizmoBox : Control
{
	private static readonly Vector2 DefaultPivot = new(0.5f, 0.5f);
	private const float HandleRadius = 16f;
	private const float SnapThreshold = 4f;
	private const float MaxMeasureDistance = 300f;

	[Export] private Label _sizeIndLabel = null!;
	[Export] private Control _pivotIndicator = null!;
	[Export] private Control _cornerTL = null!;
	[Export] private Control _cornerTR = null!;
	[Export] private Control _cornerBL = null!;
	[Export] private Control _cornerBR = null!;
	[Export] private Control _edgeTop = null!;
	[Export] private Control _edgeBottom = null!;
	[Export] private Control _edgeLeft = null!;
	[Export] private Control _edgeRight = null!;
	public UIField Target = null!;
	public UIGizmos Gizmos = null!;

	private enum Handle { TL, TR, BL, BR, Top, Bottom, Left, Right }
	private enum DragState { None, Dragging, Resizing }

	private DragState _dragState;
	private Handle _resizeCorner;
	private Vector2 _resizeStartPosOffset;
	private Vector2 _resizeStartSizeOffset;
	private Vector2 _resizeStartGlobalMouse;
	private Vector2 _totalResizeDelta;
	private Vector2 _dragGrabOffset;
	private bool _showingMeasures;
	private Vector2 _lastMeasurePos;
	private Vector2 _lastMeasureSize;
	private Vector2 _lastNudge;
	private CreatorHistory? _nudgeHistory;

	private readonly List<SnapGuide> _activeGuides = [];
	private Control? _sizeIndParent;
	private bool _hoveringGizmo;

	public override void _EnterTree()
	{
		base._EnterTree();

		if (Target == null) return;

		RegisterHandle(_cornerTL, Handle.TL);
		RegisterHandle(_cornerTR, Handle.TR);
		RegisterHandle(_cornerBL, Handle.BL);
		RegisterHandle(_cornerBR, Handle.BR);
		RegisterHandle(_edgeTop, Handle.Top);
		RegisterHandle(_edgeBottom, Handle.Bottom);
		RegisterHandle(_edgeLeft, Handle.Left);
		RegisterHandle(_edgeRight, Handle.Right);

		if (_sizeIndLabel != null)
		{
			_sizeIndParent = _sizeIndLabel.GetParent<Control>();
			_sizeIndParent.MouseFilter = MouseFilterEnum.Ignore;
		}

		Target.TransformChanged.Disconnect(OnTransformChanged);
		Target.TransformChanged.Connect(OnTransformChanged);
		GuiInput += OnGuiInput;
		OnTransformChanged();
	}

	public override void _ExitTree()
	{
		_nudgeHistory?.CommitAction();
		_nudgeHistory = null;
		EndDrag();

		Target?.TransformChanged.Disconnect(OnTransformChanged);
		GuiInput -= OnGuiInput;

		SetHoveringUIGizmo(false);
		_showingMeasures = false;
		Gizmos?.HideGuidelines();
		Gizmos?.HideMeasurements();

		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		Vector2 globalMouse = GetGlobalMousePosition();
		SetHoveringUIGizmo(IsMouseOverGizmo(globalMouse));

		HandleAltMeasures();

		if (_dragState == DragState.None)
			return;

		if (!Input.IsMouseButtonPressed(MouseButton.Left))
		{
			EndDrag();
			return;
		}

		if (_dragState == DragState.Dragging)
		{
			if (Target?.NodeControl == null) { EndDrag(); return; }

			Vector2 targetAbs = globalMouse - _dragGrabOffset;
			Vector2 snappedAbs = SnapPosition(targetAbs);
			Target.PositionOffset += (snappedAbs - Target.AbsolutePosition);
		}
		else
		{
			Vector2 globalDelta = globalMouse - _resizeStartGlobalMouse;
			Vector2 localDelta = globalDelta.Rotated(-Rotation);
			if (Scale.X != 0 && Scale.Y != 0)
				localDelta /= Scale;
			_totalResizeDelta = localDelta;
			ApplyResize();
		}
	}

	private void EndDrag()
	{
		if (_dragState == DragState.None) return;
		bool wasResizing = _dragState == DragState.Resizing;
		_dragState = DragState.None;
		Gizmos?.HideGuidelines();
		CommitHistory(wasResizing);
	}

	private bool IsMouseOverGizmo(Vector2 globalMouse)
	{
		Vector2 localPos = GetLocalMousePosition(globalMouse);
		Rect2 expanded = new(
			-HandleRadius,
			-HandleRadius,
			Size.X + HandleRadius * 2,
			Size.Y + HandleRadius * 2
		);
		return expanded.HasPoint(localPos);
	}

	private Vector2 GetLocalMousePosition() => GetLocalMousePosition(GetGlobalMousePosition());

	private Vector2 GetLocalMousePosition(Vector2 globalMouse)
	{
		Vector2 adjusted = globalMouse - GlobalPosition - PivotOffset;
		adjusted = adjusted.Rotated(-Rotation);
		if (Scale.X != 0 && Scale.Y != 0)
			adjusted /= Scale;
		return adjusted + PivotOffset;
	}

	private Vector2 SnapPosition(Vector2 proposedAbsPos)
	{
		if (Input.IsKeyPressed(Key.Shift))
		{
			Gizmos?.HideGuidelines();
			return proposedAbsPos;
		}

		_activeGuides.Clear();

		if (GetViewport() is not Viewport vp) return proposedAbsPos;
		Vector2 vpSize = vp.GetVisibleRect().Size;
		Vector2 vpRangeX = new(0, vpSize.X);
		Vector2 vpRangeY = new(0, vpSize.Y);

		Transform2D gt = Target.NodeControl.GetGlobalTransform();
		Vector2 size = Target.AbsoluteSize;
		UIBounds proposedBounds = new(
			proposedAbsPos,
			proposedAbsPos + gt.X * size.X,
			proposedAbsPos + gt.Y * size.Y,
			proposedAbsPos + gt.X * size.X + gt.Y * size.Y);
		Vector2 proposedPivot = proposedAbsPos
			+ gt.X * (Target.PivotPoint.X * size.X)
			+ gt.Y * (Target.PivotPoint.Y * size.Y);

		List<AlignmentRect> targets = CollectAlignmentTargets();
		AlignmentRect dragged = new(proposedBounds, proposedPivot, default);

		float snapDeltaX = 0;
		float snapDeltaY = 0;
		SnapGuide? guideX = null;
		SnapGuide? guideY = null;
		float bestCombinedDist = float.MaxValue;

		foreach (AlignmentRect target in targets)
		{
			float txDist = SnapThreshold + 1;
			float tyDist = SnapThreshold + 1;
			float tdx = 0, tdy = 0;
			SnapGuide? tgx = null, tgy = null;

			SnapEdgeSet(dragged, target, false, vpRangeY, target.Color,
				ref txDist, ref tdx, ref tgx);
			SnapEdgeSet(dragged, target, true, vpRangeX, target.Color,
				ref tyDist, ref tdy, ref tgy);

			if (txDist > SnapThreshold && tyDist > SnapThreshold)
				continue;

			float combined = Mathf.Abs(tdx) + Mathf.Abs(tdy);
			if (combined >= bestCombinedDist)
				continue;

			bestCombinedDist = combined;
			snapDeltaX = tdx;
			snapDeltaY = tdy;
			guideX = tgx;
			guideY = tgy;
		}

		Vector2 snappedAbs = proposedAbsPos;

		if (Mathf.Abs(snapDeltaX) <= SnapThreshold)
			snappedAbs.X += snapDeltaX;

		if (Mathf.Abs(snapDeltaY) <= SnapThreshold)
			snappedAbs.Y += snapDeltaY;

		if (guideX.HasValue) _activeGuides.Add(guideX.Value);
		if (guideY.HasValue) _activeGuides.Add(guideY.Value);

		if (!Input.IsKeyPressed(Key.Alt))
			Gizmos?.ShowGuidelines([.. _activeGuides]);
		else
			Gizmos?.HideGuidelines();

		return snappedAbs;
	}

	private static void SnapEdgeSet(
		AlignmentRect dragged, AlignmentRect target, bool isY,
		Vector2 guideRange, Color color,
		ref float bestDist, ref float bestDelta, ref SnapGuide? bestGuide)
	{
		float[] dPts = isY ? dragged.SnapY : dragged.SnapX;
		float[] tPts = isY ? target.SnapY : target.SnapX;

		foreach (float d in dPts)
		{
			foreach (float t in tPts)
			{
				float dist = Mathf.Abs(d - t);
				if (dist >= bestDist) continue;

				bestDist = dist;
				bestDelta = t - d;
				bestGuide = new SnapGuide(
					isY ? new(guideRange.X, t) : new(t, guideRange.X),
					isY ? new(guideRange.Y, t) : new(t, guideRange.Y),
					color);
			}
		}
	}

	private static Vector2 GetPivotGlobal(UIField field)
	{
		if (field?.NodeControl == null) return Vector2.Zero;
		Transform2D gt = field.NodeControl.GetGlobalTransform();
		Vector2 size = field.NodeControl.Size;
		return gt.Origin
			+ gt.X * (field.PivotPoint.X * size.X)
			+ gt.Y * (field.PivotPoint.Y * size.Y);
	}

	private List<AlignmentRect> CollectAlignmentTargets()
	{
		List<AlignmentRect> targets = [];

		if (Target.Parent is UIField parentField)
		{
			targets.Add(new AlignmentRect(GetBounds(parentField), GetPivotGlobal(parentField), new Color(0.4f, 0.7f, 1f)));
		}
		else if (Target.Parent is GUI gui && gui.GDNode is Control guiControl)
		{
			targets.Add(new AlignmentRect(guiControl.GlobalPosition, gui.AbsoluteSize, new Color(0.4f, 0.7f, 1f)));
		}

		if (Target.Parent is Instance parentInst)
		{
			foreach (Instance child in parentInst.GetChildren())
			{
				if (child == Target || child is not UIField sibling) continue;
				if (sibling.IsHidden) continue;
				targets.Add(new AlignmentRect(GetBounds(sibling), GetPivotGlobal(sibling), new Color(1f, 0.5f, 0.85f)));
			}
		}

		return targets;
	}

	private void RegisterHandle(Control? handle, Handle handleType)
	{
		if (handle == null) return;
		handle.GuiInput += e => OnHandleGuiInput(e, handleType);
		handle.MouseEntered += () => handle.Modulate = Colors.White;
		handle.MouseExited += () => handle.Modulate = new Color(1, 1, 1, 0.5f);
	}

	private void OnHandleGuiInput(InputEvent @event, Handle handle)
	{
		if (@event is not InputEventMouseButton btn || btn.ButtonIndex != MouseButton.Left || !btn.Pressed)
			return;
		StartResize(handle);
		AcceptEvent();
	}

	private void OnGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton btn || btn.ButtonIndex != MouseButton.Left || !btn.Pressed)
			return;

		Vector2 localPos = GetLocalMousePosition();
		if (!DetectHandle(localPos).HasValue)
		{
			if (!TrySelectDeepestChild())
				StartDrag();
		}

		AcceptEvent();
	}

	private bool TrySelectDeepestChild()
	{
		UIField? deepest = FindDeepestUIFieldUnderMouse(Target);
		if (deepest == null || deepest == Target) return false;

		Target.Root?.CreatorContext?.Selections.SelectOnly(deepest);
		return true;
	}

	private static UIField? FindDeepestUIFieldUnderMouse(Instance parent)
	{
		if (parent.GDNode is not Control parentControl) return null;
		Vector2 mousePos = parentControl.GetGlobalMousePosition();
		UIField? result = null;

		foreach (Instance child in parent.GetChildren())
		{
			if (child is not UIField { IsHidden: false } uiChild || uiChild.NodeControl == null) continue;

			if (uiChild.NodeControl.GetGlobalRect().HasPoint(mousePos))
			{
				UIField? deeper = FindDeepestUIFieldUnderMouse(uiChild);
				result = deeper ?? uiChild;
			}
		}

		return result;
	}

	private Handle? DetectHandle(Vector2 localPos)
	{
		if (localPos.X < HandleRadius && localPos.Y < HandleRadius)
			return Handle.TL;
		if (localPos.X > Size.X - HandleRadius && localPos.Y < HandleRadius)
			return Handle.TR;
		if (localPos.X < HandleRadius && localPos.Y > Size.Y - HandleRadius)
			return Handle.BL;
		if (localPos.X > Size.X - HandleRadius && localPos.Y > Size.Y - HandleRadius)
			return Handle.BR;

		if (localPos.Y < HandleRadius)
			return Handle.Top;
		if (localPos.Y > Size.Y - HandleRadius)
			return Handle.Bottom;
		if (localPos.X < HandleRadius)
			return Handle.Left;
		if (localPos.X > Size.X - HandleRadius)
			return Handle.Right;

		return null;
	}

	private CreatorHistory? BeginHistoryAction()
	{
		_nudgeHistory?.CommitAction();
		_nudgeHistory = null;
		return GetHistory();
	}

	private void StartDrag()
	{
		_nudgeHistory?.CommitAction();
		_nudgeHistory = null;
		CreatorHistory? history = BeginHistoryAction();
		if (history == null) return;

		_dragState = DragState.Dragging;
		_dragGrabOffset = GetGlobalMousePosition() - Target.AbsolutePosition;

		history.NewAction("Move UI Element");
		Vector2 savedPos = Target.PositionOffset;
		history.AddUndoCallback(new((_) => Target.PositionOffset = savedPos));
	}

	private void StartResize(Handle handle)
	{
		_nudgeHistory?.CommitAction();
		_nudgeHistory = null;
		CreatorHistory? history = BeginHistoryAction();
		if (history == null) return;

		_dragState = DragState.Resizing;
		_resizeCorner = handle;
		_resizeStartPosOffset = Target.PositionOffset;
		_resizeStartSizeOffset = Target.SizeOffset;
		_resizeStartGlobalMouse = GetGlobalMousePosition();
		_totalResizeDelta = Vector2.Zero;

		history.NewAction("Resize UI Element");
		Vector2 savedPos = Target.PositionOffset;
		Vector2 savedSize = Target.SizeOffset;
		history.AddUndoCallback(new((_) =>
		{
			Target.PositionOffset = savedPos;
			Target.SizeOffset = savedSize;
		}));
	}

	private void ApplyResize()
	{
		if (Target?.NodeControl == null) { EndDrag(); return; }
		Vector2 totalDelta = _totalResizeDelta;

		bool centerScale = Input.IsKeyPressed(Key.Ctrl) && Input.IsKeyPressed(Key.Shift);
		bool uniform = Input.IsKeyPressed(Key.Shift) && !Input.IsKeyPressed(Key.Ctrl);

		if (centerScale)
		{
			totalDelta *= 2f;
		}
		else if (uniform)
		{
			Vector2 startSize = _resizeStartSizeOffset;
			if (startSize.X == 0 || startSize.Y == 0) { EndDrag(); return; }
			float scaleX = (startSize.X + totalDelta.X) / startSize.X;
			float scaleY = (startSize.Y + totalDelta.Y) / startSize.Y;
			float scale = Mathf.Abs(scaleX - 1f) > Mathf.Abs(scaleY - 1f) ? scaleX : scaleY;
			totalDelta = startSize * (scale - 1f);
		}

		bool onLeft = _resizeCorner is Handle.TL or Handle.BL or Handle.Left;
		bool onRight = _resizeCorner is Handle.TR or Handle.BR or Handle.Right;
		bool onTop = _resizeCorner is Handle.TL or Handle.TR or Handle.Top;
		bool onBottom = _resizeCorner is Handle.BL or Handle.BR or Handle.Bottom;

		float dx = (onRight ? totalDelta.X : 0) - (onLeft ? totalDelta.X : 0);
		float dy = (onBottom ? totalDelta.Y : 0) - (onTop ? totalDelta.Y : 0);
		Vector2 sizeChange = new(dx, dy);

		Vector2 posChange = new(
			onLeft ? totalDelta.X : 0,
			onTop ? totalDelta.Y : 0);

		Vector2 newSizeOffset = (_resizeStartSizeOffset + sizeChange).Max(Vector2.One).Round();
		Vector2 actualSizeChange = newSizeOffset - _resizeStartSizeOffset;


		float nodeRot = Target.NodeControl.Rotation;

		Vector2 newPosOffset;
		if (centerScale)
		{
			Vector2 pivotDelta = ((Target.PivotPoint - DefaultPivot) * actualSizeChange).Rotated(nodeRot);
			newPosOffset = _resizeStartPosOffset + pivotDelta;
		}
		else
		{
			Vector2 totalPosDelta = (posChange + Target.PivotPoint * actualSizeChange).Rotated(nodeRot);
			newPosOffset = _resizeStartPosOffset + totalPosDelta;
		}

		Target.SizeOffset = newSizeOffset;
		Target.PositionOffset = newPosOffset.Round();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_dragState != DragState.None) return;
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

		Vector2 nudge = keyEvent.Keycode switch
		{
			Key.Left => new(-1, 0),
			Key.Right => new(1, 0),
			Key.Up => new(0, -1),
			Key.Down => new(0, 1),
			_ => Vector2.Zero
		};

		if (nudge == Vector2.Zero) return;

		CreatorHistory? history = GetHistory();
		if (history == null || Target == null) return;
		bool sameDirection = nudge == _lastNudge;
		_lastNudge = nudge;

		if (!sameDirection)
		{
			_nudgeHistory?.CommitAction();
			_nudgeHistory = history;
			history.NewAction("Nudge UI Element");
			Vector2 savedPos = Target.PositionOffset;
			history.AddUndoCallback(new((_) => Target.PositionOffset = savedPos));
		}

		Target.PositionOffset += nudge;
		Vector2 newPos = Target.PositionOffset;

		history.AddDoCallback(new((_) => Target.PositionOffset = newPos));

		GetViewport()?.SetInputAsHandled();
	}

	private static UIBounds GetBounds(UIField field)
	{
		if (field?.NodeControl == null) return default;
		Transform2D gt = field.NodeControl.GetGlobalTransform();
		Vector2 size = field.NodeControl.Size;
		Vector2 tl = gt.Origin;
		Vector2 tr = gt.Origin + gt.X * size.X;
		Vector2 bl = gt.Origin + gt.Y * size.Y;
		Vector2 br = gt.Origin + gt.X * size.X + gt.Y * size.Y;
		return new UIBounds(tl, tr, bl, br);
	}

	private void HandleAltMeasures()
	{
		if (Target?.NodeControl == null) return;

		if (!Input.IsKeyPressed(Key.Alt))
		{
			if (_showingMeasures)
			{
				_showingMeasures = false;
				Gizmos?.HideMeasurements();
			}
			return;
		}

		Vector2 myPos = Target.AbsolutePosition;
		Vector2 mySize = Target.AbsoluteSize;

		if (_showingMeasures && myPos == _lastMeasurePos && mySize == _lastMeasureSize)
			return;

		_lastMeasurePos = myPos;
		_lastMeasureSize = mySize;
		_showingMeasures = true;

		ShowMeasureOverlay(GetBounds(Target));
	}

	private void ShowMeasureOverlay(UIBounds myBounds)
	{
		if (GetViewport() is not Viewport vp) return;

		var measures = new List<MeasureGuide>();
		var measureColor = new Color(0.95f, 0.25f, 0.25f);
		Vector2 vpSize = vp.GetVisibleRect().Size;

		List<(UIBounds bounds, bool isParent)> candidates = [];

		if (Target.Parent is UIField { IsHidden: false } parentField)
			candidates.Add((GetBounds(parentField), true));

		if (Target.Parent is Instance parentInst)
		{
			foreach (Instance child in parentInst.GetChildren())
			{
				if (child == Target || child is not UIField ui || ui.IsHidden) continue;
				candidates.Add((GetBounds(ui), false));
			}
		}

		CreateViewportEdgeMeasure(measures, measureColor, myBounds, Direction.Left, vpSize);
		CreateViewportEdgeMeasure(measures, measureColor, myBounds, Direction.Right, vpSize);
		CreateViewportEdgeMeasure(measures, measureColor, myBounds, Direction.Top, vpSize);
		CreateViewportEdgeMeasure(measures, measureColor, myBounds, Direction.Bottom, vpSize);

		AddNearestElementMeasure(measures, measureColor, myBounds, candidates, Direction.Left);
		AddNearestElementMeasure(measures, measureColor, myBounds, candidates, Direction.Right);
		AddNearestElementMeasure(measures, measureColor, myBounds, candidates, Direction.Top);
		AddNearestElementMeasure(measures, measureColor, myBounds, candidates, Direction.Bottom);

		if (measures.Count > 0)
			Gizmos?.ShowMeasurements(measures);
		else
			Gizmos?.HideMeasurements();
	}

	private enum Direction { Left, Right, Top, Bottom }

	private static float GetGap(UIBounds my, UIBounds other, Direction dir)
	{
		return dir switch
		{
			Direction.Left => my.Rect.Position.X - other.Rect.End.X,
			Direction.Right => other.Rect.Position.X - my.Rect.End.X,
			Direction.Top => my.Rect.Position.Y - other.Rect.End.Y,
			Direction.Bottom => other.Rect.Position.Y - my.Rect.End.Y,
			_ => float.MaxValue
		};
	}

	private static void AddNearestElementMeasure(
		List<MeasureGuide> measures, Color color,
		UIBounds myBounds, List<(UIBounds bounds, bool isParent)> candidates,
		Direction dir)
	{
		float bestDist = MaxMeasureDistance;
		UIBounds? bestBounds = null;

		foreach (var (candidateBounds, _) in candidates)
		{
			float dist = GetGap(myBounds, candidateBounds, dir);
			if (dist > 0 && dist < bestDist)
			{
				bestDist = dist;
				bestBounds = candidateBounds;
			}
		}

		if (bestBounds.HasValue)
		{
			CreateEdgeMeasure(measures, color, myBounds, bestBounds.Value, dir, bestDist);
		}
	}

	private static void CreateEdgeMeasure(
		List<MeasureGuide> measures, Color color,
		UIBounds my, UIBounds other, Direction dir, float distance)
	{
		Vector2 from = Vector2.Zero, to = Vector2.Zero;

		switch (dir)
		{
			case Direction.Left:
				{
					float top = Mathf.Max(my.Rect.Position.Y, other.Rect.Position.Y);
					float bottom = Mathf.Min(my.Rect.End.Y, other.Rect.End.Y);
					float centerY = top < bottom
						? (top + bottom) * 0.5f
						: (my.Rect.Position.Y + my.Rect.End.Y) * 0.5f;
					from = new Vector2(my.Rect.Position.X, centerY);
					to = new Vector2(other.Rect.End.X, centerY);
					break;
				}
			case Direction.Right:
				{
					float top = Mathf.Max(my.Rect.Position.Y, other.Rect.Position.Y);
					float bottom = Mathf.Min(my.Rect.End.Y, other.Rect.End.Y);
					float centerY = top < bottom
						? (top + bottom) * 0.5f
						: (my.Rect.Position.Y + my.Rect.End.Y) * 0.5f;
					from = new Vector2(my.Rect.End.X, centerY);
					to = new Vector2(other.Rect.Position.X, centerY);
					break;
				}
			case Direction.Top:
				{
					float left = Mathf.Max(my.Rect.Position.X, other.Rect.Position.X);
					float right = Mathf.Min(my.Rect.End.X, other.Rect.End.X);
					float centerX = left < right
						? (left + right) * 0.5f
						: (my.Rect.Position.X + my.Rect.End.X) * 0.5f;
					from = new Vector2(centerX, my.Rect.Position.Y);
					to = new Vector2(centerX, other.Rect.End.Y);
					break;
				}
			case Direction.Bottom:
				{
					float left = Mathf.Max(my.Rect.Position.X, other.Rect.Position.X);
					float right = Mathf.Min(my.Rect.End.X, other.Rect.End.X);
					float centerX = left < right
						? (left + right) * 0.5f
						: (my.Rect.Position.X + my.Rect.End.X) * 0.5f;
					from = new Vector2(centerX, my.Rect.End.Y);
					to = new Vector2(centerX, other.Rect.Position.Y);
					break;
				}
		}

		measures.Add(new MeasureGuide(from, to, color, $"{distance:F0} px", (from + to) * 0.5f));
	}

	private static void CreateViewportEdgeMeasure(
		List<MeasureGuide> measures, Color color,
		UIBounds bounds, Direction dir, Vector2 vpSize)
	{
		Vector2 from = Vector2.Zero, to = Vector2.Zero;

		switch (dir)
		{
			case Direction.Left:
				{
					float centerY = bounds.Rect.Position.Y + bounds.Rect.Size.Y * 0.5f;
					from = new Vector2(bounds.Rect.Position.X, centerY);
					to = new Vector2(0, centerY);
					break;
				}
			case Direction.Right:
				{
					float centerY = bounds.Rect.Position.Y + bounds.Rect.Size.Y * 0.5f;
					from = new Vector2(bounds.Rect.End.X, centerY);
					to = new Vector2(vpSize.X, centerY);
					break;
				}
			case Direction.Top:
				{
					float centerX = bounds.Rect.Position.X + bounds.Rect.Size.X * 0.5f;
					from = new Vector2(centerX, bounds.Rect.Position.Y);
					to = new Vector2(centerX, 0);
					break;
				}
			case Direction.Bottom:
				{
					float centerX = bounds.Rect.Position.X + bounds.Rect.Size.X * 0.5f;
					from = new Vector2(centerX, bounds.Rect.End.Y);
					to = new Vector2(centerX, vpSize.Y);
					break;
				}
		}

		float distance = dir switch
		{
			Direction.Left => bounds.Rect.Position.X,
			Direction.Right => vpSize.X - bounds.Rect.End.X,
			Direction.Top => bounds.Rect.Position.Y,
			Direction.Bottom => vpSize.Y - bounds.Rect.End.Y,
			_ => 0
		};

		measures.Add(new MeasureGuide(from, to, color, $"{distance:F0} px", (from + to) * 0.5f));
	}

	private void CommitHistory(bool includeSize)
	{
		CreatorHistory? history = GetHistory();
		if (history == null) return;

		Vector2 savedPos = Target.PositionOffset;
		if (includeSize)
		{
			Vector2 savedSize = Target.SizeOffset;
			history.AddDoCallback(new((_) =>
			{
				Target.PositionOffset = savedPos;
				Target.SizeOffset = savedSize;
			}));
		}
		else
		{
			history.AddDoCallback(new((_) => Target.PositionOffset = savedPos));
		}
		history.CommitAction();
	}

	private CreatorHistory? GetHistory()
	{
		var history = Target?.Root?.CreatorContext?.History;
		return history;
	}

	private void SetHoveringUIGizmo(bool active)
	{
		if (_hoveringGizmo == active) return;
		_hoveringGizmo = active;
		if (Target?.Root?.CreatorContext?.Gizmos is Polytoria.Creator.Gizmos g)
			g.HoveringUIGizmo = active;
	}

	private void OnTransformChanged()
	{
		if (Target?.NodeControl == null) return;

		Size = Target.NodeControl.Size;
		PivotOffset = Target.NodeControl.PivotOffset;
		Transform2D gt = Target.NodeControl.GetGlobalTransform();
		Rotation = gt.Rotation;
		Scale = gt.Scale;
		GlobalPosition = gt.Origin;

		_sizeIndLabel.Text = $"{Target.AbsoluteSize.X}x{Target.AbsoluteSize.Y}";

		float cr = -Rotation;

		if (_sizeIndParent == null) return;
		_sizeIndParent.PivotOffset = _sizeIndParent.Size * 0.5f;
		_sizeIndParent.Rotation = cr;

		if (_pivotIndicator != null)
		{
			Vector2 pivotPixelPos = Target.PivotPoint * Size;
			_pivotIndicator.Position = pivotPixelPos - _pivotIndicator.Size * 0.5f;
		}
	}

	private readonly struct UIBounds
	{
		public readonly Rect2 Rect;
		public readonly Vector2 TL, TR, BL, BR;

		public UIBounds(Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br)
		{
			TL = tl; TR = tr; BL = bl; BR = br;
			float minX = Mathf.Min(Mathf.Min(tl.X, tr.X), Mathf.Min(bl.X, br.X));
			float maxX = Mathf.Max(Mathf.Max(tl.X, tr.X), Mathf.Max(bl.X, br.X));
			float minY = Mathf.Min(Mathf.Min(tl.Y, tr.Y), Mathf.Min(bl.Y, br.Y));
			float maxY = Mathf.Max(Mathf.Max(tl.Y, tr.Y), Mathf.Max(bl.Y, br.Y));
			Rect = new Rect2(minX, minY, maxX - minX, maxY - minY);
		}

	}

	private readonly struct AlignmentRect
	{
		public readonly float[] SnapX;
		public readonly float[] SnapY;
		public readonly Color Color;

		public AlignmentRect(Vector2 pos, Vector2 size, Color color)
		{
			SnapX = [pos.X, pos.X + size.X * 0.5f, pos.X + size.X];
			SnapY = [pos.Y, pos.Y + size.Y * 0.5f, pos.Y + size.Y];
			Color = color;
		}

		public AlignmentRect(UIBounds bounds, Vector2 pivot, Color color)
		{
			SnapX = [bounds.TL.X, bounds.TR.X, bounds.BL.X, bounds.BR.X, pivot.X];
			SnapY = [bounds.TL.Y, bounds.TR.Y, bounds.BL.Y, bounds.BR.Y, pivot.Y];
			Color = color;
		}
	}
}
