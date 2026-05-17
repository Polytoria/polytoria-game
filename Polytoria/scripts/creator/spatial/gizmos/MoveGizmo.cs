// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.Spatial;

public partial class MoveGizmo : Node, IGizmo
{
	private const float GizmoArrowOffset = Gizmos.GizmoCircleSize + 0.3f;
	private Vector3 _ivec = new(0f, 0f, -1f);
	private Vector3 _nivec = new(-1f, -1f, 0f);

	public List<Dynamic> Targets { get; set; } = [];
	public bool Visible { get; set; }
	public Gizmos? RootGizmos { get; set; }
	private ArrayMesh[] _moveGizmo = new ArrayMesh[3];
	private MeshInstance3D[] _moveGizmoInstance = new MeshInstance3D[3];
	private Camera3D GDCamera => RootGizmos!.Root.Environment.CurrentGDCamera!;
	private MoveGizmoAxis _currentAxis = MoveGizmoAxis.None;
	private StandardMaterial3D[] _gizmoColor = new StandardMaterial3D[3];
	private StandardMaterial3D[] _gizmoHoverColor = new StandardMaterial3D[3];
	private bool _isMouseDragging;
	private Vector3? _startRayOrigin;
	private Vector3? _startRayNormal;

	public event Action? DragStarted;
	public event Action? DragEnded;
	public event Action<Vector3>? Dragged;

	public enum MoveGizmoAxis
	{
		None = -1,
		MoveX,
		MoveY,
		MoveZ,
	}

	public override void _EnterTree()
	{
		CreateSurfTool();
		CreateInstances();
	}

	public override void _ExitTree()
	{
		ClearInstances();
	}

	private void CreateSurfTool()
	{
		for (int i = 0; i < 3; i++)
		{
			Color axisColor = Gizmos.AxisColors[i];
			Color axisHoverColor = Color.FromHsv(axisColor.H, 0.25f, 1f);

			StandardMaterial3D material = new()
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				RenderPriority = (int)Godot.Material.RenderPriorityMax,
				NoDepthTest = true,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoColor = axisColor
			};

			StandardMaterial3D materialHover = (StandardMaterial3D)material.Duplicate();
			materialHover.AlbedoColor = axisHoverColor;

			_gizmoColor[i] = material;
			_gizmoHoverColor[i] = materialHover;

			_moveGizmo[i] = new();

			SurfaceTool surftool = new();
			surftool.Begin(Godot.Mesh.PrimitiveType.Triangles);

			Vector3[] arrow = [
					_nivec * 0f + _ivec * 0f,
						_nivec * 0.01f + _ivec * 0f,
						_nivec * 0.01f + _ivec * GizmoArrowOffset,
						_nivec * 0.065f + _ivec * GizmoArrowOffset,
						_nivec * 0f + _ivec * (GizmoArrowOffset + Gizmos.GizmoArrowSize),
					];

			int arrowPoints = 5;
			int arrowSides = 16;

			float arrowSidesStep = Mathf.Tau / arrowSides;

			for (int k = 0; k < arrowSides; k++)
			{
				Basis ma = new(_ivec, k * arrowSidesStep);
				Basis mb = new(_ivec, (k + 1) * arrowSidesStep);

				for (int j = 0; j < arrowPoints - 1; j++)
				{
					Vector3[] points = [
								ma.Xform(arrow[j]),
								mb.Xform(arrow[j]),
								mb.Xform(arrow[j + 1]),
								ma.Xform(arrow[j + 1]),
							];

					surftool.AddVertex(points[0]);
					surftool.AddVertex(points[1]);
					surftool.AddVertex(points[2]);

					surftool.AddVertex(points[0]);
					surftool.AddVertex(points[2]);
					surftool.AddVertex(points[3]);
				}
			}

			surftool.SetMaterial(material);
			surftool.Commit(_moveGizmo[i]);
		}
	}

	private void CreateInstances()
	{
		for (int i = 0; i < 3; i++)
		{
			_moveGizmoInstance[i] = new MeshInstance3D
			{
				Mesh = _moveGizmo[i],
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Visible = false,
				// not using 1 because of decal wrapping onto gizmos
				Layers = 1 << 6
			};
			AddChild(_moveGizmoInstance[i]);
		}
	}

	private void ClearInstances()
	{
		for (int i = 0; i < 3; i++)
		{
			_moveGizmoInstance[i].QueueFree();
		}
	}

	public override void _Process(double delta)
	{
		SetVisiblity();
		RedrawGizmo();
	}

	public override void _Input(InputEvent @event)
	{
		if (Targets.Count == 0) return;

		Vector2 mousePos = GDCamera.GetViewport().GetMousePosition();
		Vector3 rayOrigin = GDCamera.ProjectRayOrigin(mousePos);
		Vector3 rayNormal = GDCamera.ProjectRayNormal(mousePos);
		Vector3 cameraNormal = -GDCamera.GlobalBasis.Column2;

		if (@event is InputEventMouseButton btn)
		{
			if (btn.ButtonIndex != MouseButton.Left) return;
			if (btn.Pressed)
			{
				if (_currentAxis == MoveGizmoAxis.None) return;
				if (!Visible) return;

				_startRayOrigin = rayOrigin;
				_startRayNormal = rayNormal;
				DragStarted?.Invoke();
				_isMouseDragging = true;
			}
			else
			{
				if (_isMouseDragging)
				{
					DragEnded?.Invoke();
					_isMouseDragging = false;
				}
			}
		}
		else if (@event is InputEventMouseMotion)
		{
			if (!Visible) return;
			if (_isMouseDragging)
			{
				if (_currentAxis != MoveGizmoAxis.None)
				{
					DragTransform(rayOrigin, rayNormal, cameraNormal);
				}
			}
			else
			{
				UpdateAxis(rayOrigin, rayNormal);
			}
		}
		base._Input(@event);
	}

	private void RedrawGizmo()
	{
		if (Targets.Count == 0) return;
		if (!Visible) return;

		Transform3D pform = Gizmos.GetCenterPivot([.. Targets]);
		float gizmoScale = pform.Origin.DistanceTo(GDCamera.GlobalPosition) * 0.12f;
		Vector3 pScale = new(gizmoScale, gizmoScale, gizmoScale);

		for (int i = 0; i < 3; i++)
		{
			Transform3D axisTransform = new();

			if (pform.Basis.GetColumn(i).Normalized()
				.Dot(pform.Basis.GetColumn((i + 1) % 3).Normalized()) < 1f)
			{
				axisTransform = axisTransform.LookingAt(
					pform.Basis.GetColumn(i).Normalized(),
					pform.Basis.GetColumn((i + 1) % 3).Normalized()
				);
			}

			axisTransform.Basis = axisTransform.Basis.Scaled(pScale);
			axisTransform.Origin = pform.Origin;

			_moveGizmoInstance[i].Transform = axisTransform;
		}
	}

	private void SetVisiblity()
	{
		for (int i = 0; i < 3; i++)
		{
			_moveGizmoInstance[i].Visible = Visible;
		}
	}

	private void UpdateAxis(Vector3 rayOrigin, Vector3 rayNormal)
	{
		Transform3D pivot = Gizmos.GetCenterPivot([.. Targets]);
		float gizmoScale = pivot.Origin.DistanceTo(GDCamera.GlobalPosition) * 0.12f;

		float colD = 1e20f;
		int colAxis = -1;

		for (int i = 0; i < 3; i++)
		{
			Vector3 grabberPos = pivot.Origin + pivot.Basis.GetColumn(i).Normalized() * gizmoScale * (GizmoArrowOffset + Gizmos.GizmoArrowSize * 0.5f);
			float grabberRadius = gizmoScale * Gizmos.GizmoArrowSize;

			Vector3[] result = Geometry3D.SegmentIntersectsSphere(rayOrigin, rayOrigin + rayNormal * Gizmos.MaxZ, grabberPos, grabberRadius);

			if (result.Length > 0)
			{
				float d = result[0].DistanceTo(rayOrigin);

				if (d < colD)
				{
					colD = d;
					colAxis = i;
				}
			}
		}

		HighlightAxis(colAxis);
	}

	private void HighlightAxis(int axis)
	{
		for (int i = 0; i < 3; i++)
		{
			_moveGizmo[i].SurfaceSetMaterial(0, i == axis ? _gizmoHoverColor[i] : _gizmoColor[i]);
		}

		_currentAxis = (MoveGizmoAxis)axis;
		if (RootGizmos != null)
		{
			if (_currentAxis != MoveGizmoAxis.None)
			{
				RootGizmos.HoveringGizmos = true;
			}
			else
			{
				RootGizmos.HoveringGizmos = false;
			}
		}
	}

	private void DragTransform(Vector3 rayOrigin, Vector3 rayNormal, Vector3 cameraNormal)
	{
		Transform3D pivot = Gizmos.GetCenterPivot([.. Targets]);

		Vector3 motionMask = pivot.Basis.GetColumn((int)_currentAxis).Normalized();
		Plane plane = new(motionMask.Cross(motionMask.Cross(cameraNormal)).Normalized(), pivot.Origin);

		Vector3? intersection = plane.IntersectsRay(rayOrigin, rayNormal);
		Vector3? click = plane.IntersectsRay(_startRayOrigin!.Value, _startRayNormal!.Value);

		if (intersection == null || click == null)
			return;

		Vector3 motion = motionMask.Dot(intersection.Value - click.Value) * motionMask;

		Dragged?.Invoke(motion);
	}
}
