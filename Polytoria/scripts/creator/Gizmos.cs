// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Spatial;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Utils;
using System.Collections.Generic;

namespace Polytoria.Creator;

public sealed partial class Gizmos : Node
{
	public const float GizmoCircleSize = 1.1f;
	public const float GizmoArrowSize = 0.35f;
	public const float MaxZ = 1000000f;

	public World Root = null!;
	private readonly Dictionary<Dynamic, SelectionBox> _selectionBoxes = [];

	private Camera3D _camera = null!;
	private float _gizmoScale;
	private bool _isMouseDragging;
	private bool _isDraggingDyn;
	private bool _isDragPending;
	private Vector2 _dragStartPos;
	private const float DragThreshold = 8f;
	private Dynamic? _lastHovered;
	private SelectionBox _paintBox = null!;
	private SelectionBox _hoverBox = null!;

	public bool HoveringGizmos { get; set; }
	public bool IsDraggingDynamic => _isDraggingDyn;

	public static Color[] AxisColors { get; private set; } =
	[
			new(0.96f, 0.20f, 0.32f),
			new(0.53f, 0.84f, 0.01f),
			new(0.16f, 0.55f, 0.96f),
	];

	public MoveGizmo Move = new();
	public RotateGizmo Rotate = new();
	public ScaleGizmo Scale = new();
	public ResizeGizmo Resize = new();

	private bool _historyRecording;

	public List<Dynamic> Selected = [];
	public List<Dynamic> DragSelected = [];

	private readonly Dictionary<Dynamic, Vector3> _dragStartOffsets = [];
	private readonly Dictionary<Dynamic, Transform3D> _initialRelativeTransforms = [];
	private Transform3D _pivotStart;
	private CreatorHistory _history = null!;

	public void Attach(World game)
	{
		Root = game;
		game.Loaded.Once(() =>
		{
			_history = Root.CreatorContext.History;
		});
	}

	public override void _Ready()
	{
		_camera = Root.CreatorContext.Freelook.Camera3D;
		Move.RootGizmos = this;
		Rotate.RootGizmos = this;
		Scale.RootGizmos = this;
		Resize.RootGizmos = this;
		Move.Name = "Move";
		Rotate.Name = "Rotate";
		Scale.Name = "Scale";
		Resize.Name = "Resize";

		Move.DragStarted += OnMoveDragStarted;
		Move.DragEnded += OnMoveDragEnded;
		Move.Dragged += OnMoveDragged;

		Rotate.DragStarted += OnRotateDragStarted;
		Rotate.DragEnded += OnRotateDragEnded;
		Rotate.Dragged += OnRotateDragged;

		Scale.DragStarted += OnScaleDragStarted;
		Scale.DragEnded += OnScaleDragEnded;
		Scale.Dragged += OnScaleDragged;

		Resize.DragStarted += OnResizeDragStarted;
		Resize.DragEnded += OnResizeDragEnded;
		Resize.Dragged += OnResizeDragged;

		AddChild(Move, true);
		AddChild(Rotate, true);
		AddChild(Scale, true);
		AddChild(Resize, true);
		AddChild(_paintBox = new() { Root = Root, Name = "PaintBox", RootGizmos = this });
		AddChild(_hoverBox = new() { Root = Root, Name = "HoverBox", RootGizmos = this });
	}

	private void OnResizeDragStarted()
	{
		_pivotStart = Selected[0].GetGlobalTransform();
		_history.NewAction("Resize Transform");
		RecordHistoryUndo();
	}

	private void OnResizeDragged(ResizeGizmo.ResizeGizmoAxis currentAxis, Vector3 rawMotion)
	{
		float moveSnap = CreatorService.Interface.MoveSnapping;
		bool isAltPressed = Input.IsActionPressed("gizmo_scale_uniform");
		bool isShiftPressed = Input.IsKeyPressed(Key.Shift);

		float scaleFactor = isAltPressed ? 2.0f : 1.0f;

		// 0 means x, 1 means y, 2 means z
		int column = (int)currentAxis >> 1;
		// -1 means negative direction, 1 means positive direction
		int globalDirection = ((int)currentAxis & 1) == 1 ? 1 : -1;

		Dynamic selectedItem = Selected[0];
		Vector3 oldOrigin = _pivotStart.Origin;
		Basis newBasis = _pivotStart.Basis;

		Vector3 oldScaleVector = _pivotStart.Basis[column];
		Vector3 currentAxisVector = oldScaleVector * globalDirection;
		int localDirection = rawMotion.Dot(currentAxisVector) > 0 ? 1 : -1;

		float snappedDelta = rawMotion.Snap(moveSnap).Length() * localDirection;
		Vector3 resizeDirection = oldScaleVector.Normalized();
		Vector3 newScaleVector = oldScaleVector + resizeDirection * snappedDelta * scaleFactor;

		// Apply minimum size & prevent negative scale
		float newLength = newScaleVector.Length();

		if (newLength < moveSnap || newScaleVector.Dot(resizeDirection) < 0)
		{
			newLength = moveSnap;
			newScaleVector = resizeDirection * moveSnap;
			snappedDelta = (moveSnap - oldScaleVector.Length()) / scaleFactor;
		}

		float ratio = newLength / oldScaleVector.Length();

		Vector3 totalOriginOffset = Vector3.Zero;

		for (int i = 0; i < 3; i++)
		{
			if (i == column)
			{
				// Primary Axis
				newBasis[i] = newScaleVector;
				if (!isAltPressed)
				{
					totalOriginOffset += (globalDirection * snappedDelta * _pivotStart.Basis[i].Normalized() / 2);
				}
			}
			else if (isShiftPressed)
			{
				// Uniform Scaling for other axes
				float oldColLength = _pivotStart.Basis[i].Length();
				float newColLength = oldColLength * ratio;
				newBasis[i] = _pivotStart.Basis[i].Normalized() * newColLength;
			}
		}

		Transform3D newTransform = new(newBasis, oldOrigin + totalOriginOffset);
		selectedItem.SetGlobalTransform(newTransform);
	}

	private void OnResizeDragEnded()
	{
		CommitHistorySelectedTransform();
	}

	private void OnScaleDragStarted()
	{
		_pivotStart = GetSelectionPivot();
		_initialRelativeTransforms.Clear();

		// Store each object's transform relative to pivot
		foreach (Dynamic item in Selected)
		{
			Transform3D itemTransform = item.GetGlobalTransform();
			Transform3D relative = _pivotStart.AffineInverse() * itemTransform;
			_initialRelativeTransforms[item] = relative;
		}
		_history.NewAction("Scale Transform");
		RecordHistoryUndo();
	}

	private void OnScaleDragged(Vector3 vector)
	{
		Vector3 scaleFactors;
		float snapValue = CreatorService.Interface.MoveSnapping / 10.0f;
		if (Input.IsActionPressed("gizmo_scale_uniform"))
		{
			float maxChange = vector.X;
			if (Mathf.Abs(vector.Y) > Mathf.Abs(maxChange)) maxChange = vector.Y;
			if (Mathf.Abs(vector.Z) > Mathf.Abs(maxChange)) maxChange = vector.Z;

			float snappedChange = Mathf.Snapped(maxChange, snapValue);
			scaleFactors = Vector3.One * (1.0f + snappedChange);
		}
		else
		{
			scaleFactors = Vector3.One + vector.Snap(CreatorService.Interface.MoveSnapping / 10);
		}
		Basis scaledBasis = _pivotStart.Basis.Scaled(new(
			Mathf.Max(0.01f, scaleFactors.X),
			Mathf.Max(0.01f, scaleFactors.Y),
			Mathf.Max(0.01f, scaleFactors.Z)
			));

		Transform3D scaledPivot = new(
			scaledBasis,
			_pivotStart.Origin
		);

		foreach ((Dynamic item, Transform3D relative) in _initialRelativeTransforms)
		{
			item.SetGlobalTransform(scaledPivot * relative);
		}
	}

	private void OnScaleDragEnded()
	{
		CommitHistorySelectedTransform();
		_initialRelativeTransforms.Clear();
	}

	private void OnRotateDragStarted()
	{
		_pivotStart = GetSelectionPivot();
		_initialRelativeTransforms.Clear();

		// Store each object's transform relative to pivot
		foreach (Dynamic item in Selected)
		{
			Transform3D itemTransform = item.GetGlobalTransform();
			Transform3D relative = _pivotStart.AffineInverse() * itemTransform;
			_initialRelativeTransforms[item] = relative;
		}
		_history.NewAction("Rotate Transform");
		RecordHistoryUndo();
	}

	private void OnRotateDragged(Basis basis)
	{
		basis = SnapBasis(basis, _pivotStart.Basis, CreatorService.Interface.RotateSnapping);

		Transform3D rotatedPivot = new(basis, _pivotStart.Origin);

		foreach ((Dynamic item, Transform3D relative) in _initialRelativeTransforms)
		{
			item.SetGlobalTransform(rotatedPivot * relative);
		}
	}

	private void OnRotateDragEnded()
	{
		CommitHistorySelectedTransform();
		_initialRelativeTransforms.Clear();
	}

	private static Basis SnapBasis(Basis basis, Basis originalBasis, float deg)
	{
		float snapAngle = Mathf.DegToRad(deg);

		Basis deltaBasis = basis * originalBasis.Inverse();

		Quaternion quat = new(deltaBasis);
		Vector3 axis = quat.GetAxis();
		float angle = quat.GetAngle();

		float snappedAngle = Mathf.Round(angle / snapAngle) * snapAngle;

		Basis snappedDelta = new(axis, snappedAngle);
		return snappedDelta * originalBasis;
	}

	private void OnMoveDragged(Vector3 vector)
	{
		foreach (Dynamic item in Selected)
		{
			if (_dragStartOffsets.TryGetValue(item, out Vector3 offset))
			{
				item.Position = vector.Snap(CreatorService.Interface.MoveSnapping) + offset;
			}
		}
	}

	private void OnMoveDragEnded()
	{
		CommitHistorySelectedTransform();
	}

	private void OnMoveDragStarted()
	{
		_dragStartOffsets.Clear();

		foreach (Dynamic item in Selected)
		{
			_dragStartOffsets[item] = item.Position;
		}

		_history.NewAction("Move Transform");
		RecordHistoryUndo();
	}

	private void RecordHistoryUndo()
	{
		foreach (Dynamic item in Selected)
		{
			Transform3D t = item.GetGlobalTransform();
			_history.AddUndoCallback(new((_) =>
			{
				item.SetGlobalTransform(t);
			}));
		}
	}

	private void CommitHistorySelectedTransform()
	{
		foreach (Dynamic item in Selected)
		{
			Transform3D t = item.GetGlobalTransform();
			_history.AddDoCallback(new((_) =>
			{
				item.SetGlobalTransform(t);
			}));

			// Call update bounds on finish
			item.PropagateUpdateCreatorBounds();
		}
		_history.CommitAction();
	}

	public override void _Process(double delta)
	{
		bool sv = true;

		if (Selected.Count == 0) sv = false;

		Move.Visible = CreatorService.Interface.ToolMode == ToolModeEnum.Move && sv;
		Rotate.Visible = CreatorService.Interface.ToolMode == ToolModeEnum.Rotate && sv;

		if (CreatorService.Interface.ToolMode == ToolModeEnum.Scale && sv)
		{
			bool sr = false;
			if (Selected.Count == 1 && Selected[0] is Part)
			{
				sr = true;
			}
			Resize.Visible = sr;
			Scale.Visible = !sr;
		}
		else
		{
			Resize.Visible = false;
			Scale.Visible = false;
		}
	}

	public void Select(Dynamic dyn)
	{
		SelectionBox box = new()
		{
			Root = Root,
			Target = dyn,
			RootGizmos = this
		};
		AddChild(box);
		_selectionBoxes[dyn] = box;
		Selected.Add(dyn);
		Move.Targets.Add(dyn);
		Rotate.Targets.Add(dyn);
		Scale.Targets.Add(dyn);
		Resize.Targets.Add(dyn);
	}

	public void Deselect(Dynamic dyn)
	{
		if (_selectionBoxes.TryGetValue(dyn, out SelectionBox? box))
		{
			box.Target = null;
			_selectionBoxes.Remove(dyn);
			box.QueueFree();
		}
		if (Selected.Contains(dyn))
		{
			// Reset hover gizmo state when deselected
			HoveringGizmos = false;
		}
		Selected.Remove(dyn);
		Move.Targets.Remove(dyn);
		Rotate.Targets.Remove(dyn);
		Scale.Targets.Remove(dyn);
		Resize.Targets.Remove(dyn);
	}

	public static Instance? GetModelRoot(Instance instance)
	{
		Instance? current = instance;
		Instance? topModel = instance;

		while (current != null)
		{
			if (current.ModelRoot != null)
				topModel = current.ModelRoot;
			else if (current is Model model)
				topModel = model;
			else if (current is Physical phy)
				topModel = phy;
			current = current.Parent;
		}

		return topModel;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Root.CreatorContext.IsViewportFocused) { return; }
		ToolModeEnum toolMode = CreatorService.Interface.ToolMode;

		Vector2 mousePos = _camera.GetViewport().GetMousePosition();

		Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
		Vector3 rayNormal = rayOrigin + _camera.ProjectRayNormal(mousePos) * 1000;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayNormal);
		query.CollideWithAreas = true;
		query.CollideWithBodies = false;
		query.CollisionMask = (1 << 2) | (1 << 3);

		Godot.Collections.Dictionary? intersection = Root.World3D.DirectSpaceState.IntersectRay(query);

		Dynamic? hoveringOn = null;
		if (intersection.Count > 0)
		{
			hoveringOn = Dynamic.GetDynFromCreatorBounds((Node)intersection["collider"]);
		}

		if (toolMode == ToolModeEnum.Paint)
		{
			if (hoveringOn != null && hoveringOn is Part && !hoveringOn.Locked)
			{
				_paintBox.SelectionColor = CreatorService.Interface.TargetPartColor;
				_paintBox.Target = hoveringOn;
			}
			else
			{
				_paintBox.Target = null;
			}
		}

		Instance? selectInstance = null;

		if (hoveringOn != null)
		{
			selectInstance = Input.IsKeyPressed(Key.Alt) ? hoveringOn : GetModelRoot(hoveringOn) ?? hoveringOn;
		}

		if (selectInstance is Dynamic sdyn && !sdyn.Locked)
		{
			_hoverBox.Target = sdyn;
		}
		else
		{
			_hoverBox.Target = null;
		}

		// Select shortcuts
		if (toolMode == ToolModeEnum.Select)
		{
			if (@event.IsActionPressed("gizmo_rotate"))
			{
				RotateSelectedAround(90);
				DragSelectedDynamics();
			}
			if (@event.IsActionPressed("gizmo_tilt"))
			{
				TiltSelectedAround(90);
				DragSelectedDynamics();
			}
		}

		if (@event is InputEventMouseButton button)
		{
			if (HoveringGizmos) { return; }
			if (button.ButtonIndex != MouseButton.Left) { return; }
			if (button.Pressed)
			{
				_isMouseDragging = true;
				_dragStartPos = button.Position;
			}
			else
			{
				_isMouseDragging = false;
				_isDragPending = false;
				if (_isDraggingDyn)
				{
					_isDraggingDyn = false;
					CommitHistorySelectedTransform();
				}
				DragSelected.Clear();
				return;
			}
			bool isMultiSelect = Input.IsActionPressed("gizmo_multi_select");

			if (hoveringOn != null)
			{
				// Force select NPC instead of CharacterModel
				if (selectInstance?.Parent is NPC)
				{
					selectInstance = selectInstance.Parent;
				}

				// Don't select creator freelook/current cam
				if (selectInstance == Root.Environment.CurrentCamera)
				{
					selectInstance = null;
				}

				if (selectInstance != null && selectInstance is Dynamic targetDyn)
				{
					if (targetDyn.Locked && !isMultiSelect)
					{
						Root.CreatorContext.Selections.DeselectAll();
						return;
					}

					if (isMultiSelect)
					{
						if (Root.CreatorContext.Selections.HasSelected(targetDyn))
						{
							Root.CreatorContext.Selections.Deselect(targetDyn);
						}
						else
						{
							ProcessPaint(hoveringOn);
							Root.CreatorContext.Selections.Select(targetDyn);
						}
					}
					else
					{
						ProcessPaint(hoveringOn);
						Root.CreatorContext.Selections.SelectOnly(targetDyn);
					}

					if (toolMode == ToolModeEnum.Select)
					{
						DragSelected.Add(targetDyn);
						_isDragPending = true;
					}
				}
			}
			else
			{
				if (!isMultiSelect)
				{
					Root.CreatorContext.Selections.DeselectAll();
				}
			}
		}
		else if (@event is InputEventMouseMotion motion)
		{
			if (_isDragPending && !_isDraggingDyn)
			{
				float distance = motion.Position.DistanceTo(_dragStartPos);
				if (distance >= DragThreshold)
				{
					_isDraggingDyn = true;
					_isDragPending = false;
					_history.NewAction("Select Drag Transform");
					RecordHistoryUndo();
				}
			}

			if (_isDraggingDyn)
			{
				DragSelectedDynamics();
			}
		}

		_lastHovered = hoveringOn;
	}

	private void RotateSelectedAround(float angle)
	{
		Transform3D t = GetCenterPivot([.. Selected]);
		Vector3 pivotPosition = t.Origin;
		float rotateAngle = Mathf.DegToRad(angle);

		foreach (Dynamic item in Selected)
		{
			Vector3 relativePos = item.GetGlobalPosition() - pivotPosition;
			Transform3D rotation = Transform3D.Identity.Rotated(Vector3.Up, rotateAngle);
			Vector3 rotatedPos = rotation * relativePos;

			item.SetGlobalPosition(pivotPosition + rotatedPos);
			item.GDNode3D.Rotation += new Vector3(0, rotateAngle, 0);

			if (_selectionBoxes.TryGetValue(item, out var box)) box.InvalidateBoundCache();
			item.UpdateCurrentTransformCache();
		}
	}

	private void TiltSelectedAround(float angle)
	{
		Transform3D t = GetCenterPivot([.. Selected]);
		Vector3 pivotPosition = t.Origin;
		float tiltAngle = Mathf.DegToRad(angle);

		Vector3 cameraPosition = GetViewport().GetCamera3D().GlobalPosition;
		Vector3 directionToCamera = (cameraPosition - pivotPosition).Normalized();

		directionToCamera.Y = 0;
		directionToCamera = directionToCamera.Normalized();

		float angleToCamera = Mathf.Atan2(directionToCamera.X, directionToCamera.Z);
		float snappedAngle = Mathf.Round(angleToCamera / (Mathf.Pi / 2)) * (Mathf.Pi / 2);

		Vector3 tiltAxis = new(Mathf.Cos(snappedAngle), 0, Mathf.Sin(snappedAngle));

		foreach (Dynamic item in Selected)
		{
			Vector3 relativePos = item.GetGlobalPosition() - pivotPosition;
			Transform3D rotation = Transform3D.Identity.Rotated(tiltAxis, tiltAngle);
			Vector3 rotatedPos = rotation * relativePos;
			item.SetGlobalPosition(pivotPosition + rotatedPos);

			// Apply rotation to the object itself
			item.GDNode3D.Rotate(tiltAxis, tiltAngle);

			if (_selectionBoxes.TryGetValue(item, out var box)) box.InvalidateBoundCache();
			item.UpdateCurrentTransformCache();
		}
	}

	private void ProcessPaint(Dynamic dyn)
	{
		if (dyn is Part p)
		{
			CreatorHistory history = Root.CreatorContext.History;
			if (CreatorService.Interface.ToolMode == ToolModeEnum.Paint)
			{
				Color oldC = p.Color;
				Color newC = CreatorService.Interface.TargetPartColor;
				history.NewAction("Paint Part");
				history.AddDoCallback(new((_) =>
				{
					p.Color = newC;
				}));
				history.AddUndoCallback(new((_) =>
				{
					p.Color = oldC;
				}));
				history.CommitAction();
			}
			else if (CreatorService.Interface.ToolMode == ToolModeEnum.Brush)
			{
				Part.PartMaterialEnum oldC = p.Material;
				Part.PartMaterialEnum newC = CreatorService.Interface.TargetPartMaterial;
				history.NewAction("Brush Part");
				history.AddDoCallback(new((_) =>
				{
					p.Material = newC;
				}));
				history.AddUndoCallback(new((_) =>
				{
					p.Material = oldC;
				}));
				history.CommitAction();
			}
		}
	}

	private void DragSelectedDynamics()
	{
		Vector2 mousePos = _camera.GetViewport().GetMousePosition();
		Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
		Vector3 rayNormal = rayOrigin + _camera.ProjectRayNormal(mousePos) * 1000;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayNormal);
		query.CollideWithBodies = false;
		query.CollideWithAreas = true;
		query.CollisionMask = 1 << 3;

		Godot.Collections.Array<Rid> excludeArray = [];

		foreach (Dynamic item in DragSelected)
		{
			if (item is Physical p)
			{
				excludeArray.Add(p.GetRid());
			}
			// Add Descendants
			foreach (Instance n in item.GetDescendants())
			{
				if (n is Physical p2)
				{
					excludeArray.Add(p2.GetRid());
				}
				if (n is Dynamic d)
				{
					excludeArray.Add(d.GetBoundRid());
				}
			}
			excludeArray.Add(item.GetBoundRid());
			item.UpdateCreatorBounds();
		}

		query.Exclude = excludeArray;

		Godot.Collections.Dictionary intersection = Root.World3D.DirectSpaceState.IntersectRay(query);

		if (intersection.Count > 0)
		{
			Vector3 pos = (Vector3)intersection["position"];
			Vector3 hitNormal = (Vector3)intersection["normal"];
			float snapAmount = CreatorService.Interface.MoveSnapping;

			foreach (Dynamic item in DragSelected)
			{
				Aabb bounds = item.CreatorBounds;
				Vector3 center = bounds.GetCenter();

				Vector3 surfacePoint = center;

				if (Mathf.Abs(hitNormal.X) > 0.5f)
					surfacePoint.X = hitNormal.X > 0 ? bounds.Position.X : bounds.End.X;

				if (Mathf.Abs(hitNormal.Y) > 0.5f)
					surfacePoint.Y = hitNormal.Y > 0 ? bounds.Position.Y : bounds.End.Y;

				if (Mathf.Abs(hitNormal.Z) > 0.5f)
					surfacePoint.Z = hitNormal.Z > 0 ? bounds.Position.Z : bounds.End.Z;

				Vector3 pivotOffset = item.GetGlobalPosition() - surfacePoint;

				Vector3 snappedHitPos = new(
					Mathf.Abs(hitNormal.X) > 0.9f ? pos.X : Mathf.Snapped(pos.X, snapAmount),
					Mathf.Abs(hitNormal.Y) > 0.9f ? pos.Y : Mathf.Snapped(pos.Y, snapAmount),
					Mathf.Abs(hitNormal.Z) > 0.9f ? pos.Z : Mathf.Snapped(pos.Z, snapAmount)
				);

				item.SetGlobalPosition(snappedHitPos + pivotOffset);
				item.UpdateCurrentTransformCache();
			}
		}
	}

	public static Transform3D GetCenterPivot(Instance[] instances)
	{
		Vector3 center = Vector3.Zero;
		int count = 0;
		foreach (Instance sel in instances)
		{
			if (sel is Dynamic dyn)
			{
				Transform3D xform = dyn.GetGlobalTransform();
				center += xform.Origin;
				count++;
			}
		}
		center /= count;

		return new Transform3D(Basis.Identity, center);
	}

	private Transform3D GetSelectionPivot()
	{
		if (Selected.Count == 0) return Transform3D.Identity;

		return GetCenterPivot([.. Selected]);
	}
}
