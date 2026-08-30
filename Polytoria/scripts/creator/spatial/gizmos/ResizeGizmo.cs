// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.Spatial;

public partial class ResizeGizmo : Node, IGizmo
{
	private Vector3 _ivec = new(0f, 0f, -1f);
	private Vector3 _nivec = new(-1f, -1f, 0f);

	public List<Dynamic> Targets { get; set; } = [];
	public bool Visible { get; set; }
	public Gizmos? RootGizmos { get; set; }

	private SphereMesh[] _resizeGizmo = new SphereMesh[6];
	private MeshInstance3D[] _resizeGizmoInstance = new MeshInstance3D[6];

	private StandardMaterial3D[] _gizmoColor = new StandardMaterial3D[6];
	private StandardMaterial3D[] _gizmoHoverColor = new StandardMaterial3D[6];

	private Camera3D GDCamera => RootGizmos!.Root.Environment.CurrentGDCamera!;
	private ResizeGizmoAxis _currentAxis = ResizeGizmoAxis.None;

	private bool _isMouseDragging;
	private Vector3? _startRayOrigin;
	private Vector3? _startRayNormal;
	private float _gizmoScale;

	public event Action? DragStarted;
	public event Action? DragEnded;
	public event Action<ResizeGizmoAxis, Vector3>? Dragged;

	public enum ResizeGizmoAxis
	{
		None = -1,
		Left,
		Right,
		Bottom,
		Top,
		Front,
		Back,
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
		for (int i = 0; i < 6; i++)
		{
			Color axisColor = Gizmos.AxisColors[i >> 1];

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

			_resizeGizmo[i] = new()
			{
				Material = material,
				Radius = 0.1f,
				Height = 0.2f
			};
		}
	}

	private void CreateInstances()
	{
		for (int i = 0; i < 6; i++)
		{
			_resizeGizmoInstance[i] = new MeshInstance3D
			{
				Mesh = _resizeGizmo[i],
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Visible = false,
				// not using 1 because of decal wrapping onto gizmos
				Layers = 1 << 6
			};
			AddChild(_resizeGizmoInstance[i]);
		}
	}

	private void ClearInstances()
	{
		for (int i = 0; i < 6; i++)
		{
			_resizeGizmoInstance[i].QueueFree();
		}
	}

	public override void _Process(double delta)
	{
		if (_isMouseDragging && !Input.IsMouseButtonPressed(MouseButton.Left))
		{
			DragEnded?.Invoke();
			_isMouseDragging = false;
		}

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
				if (_currentAxis == ResizeGizmoAxis.None) return;
				if (!Visible) return;
				_startRayOrigin = rayOrigin;
				_startRayNormal = rayNormal;
				DragStarted?.Invoke();
				_isMouseDragging = true;
				RootGizmos?.HoveringGizmos = true;
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
				if (_currentAxis != ResizeGizmoAxis.None)
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

		Dynamic targetDynamic = Targets[0];
		Transform3D targetTransform = targetDynamic.GetGlobalTransform();

		Vector3 half = targetDynamic.Size * 0.5f;

		Vector3 worldCenter = targetTransform.Origin;
		Basis targetRotation = targetTransform.Basis.Orthonormalized();

		Vector3[] localOffsets =
		[
			new(-half.X, 0, 0),
			new(+half.X, 0, 0),
			new(0, -half.Y, 0),
			new(0, +half.Y, 0),
			new(0, 0, -half.Z),
			new(0, 0, +half.Z),
		];

		// Place each gizmo
		for (int i = 0; i < 6; i++)
		{
			Transform3D gizmoTransform = new()
			{
				Basis = targetRotation,
				Origin = worldCenter + targetRotation.Xform(localOffsets[i])
			};

			float gizmoScale = gizmoTransform.Origin.DistanceTo(GDCamera.GlobalPosition) * 0.12f;
			gizmoTransform.Basis = gizmoTransform.Basis.Scaled(new Vector3(gizmoScale, gizmoScale, gizmoScale));

			_resizeGizmoInstance[i].GlobalTransform = gizmoTransform;
		}

	}


	private void SetVisiblity()
	{
		for (int i = 0; i < 6; i++)
		{
			_resizeGizmoInstance[i].Visible = Visible;
		}
	}

	private void UpdateAxis(Vector3 rayOrigin, Vector3 rayNormal)
	{
		Transform3D pivot = Gizmos.GetCenterPivot([.. Targets]);
		_gizmoScale = pivot.Origin.DistanceTo(GDCamera.GlobalPosition) * 0.12f;

		float colD = 1e20f;
		int colAxis = -1;

		for (int i = 0; i < 6; i++)
		{
			Vector3 grabberPos = _resizeGizmoInstance[i].GlobalPosition;
			float grabberRadius = _gizmoScale * Gizmos.GizmoArrowSize;

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
		for (int i = 0; i < 6; i++)
		{
			_resizeGizmo[i].SurfaceSetMaterial(0, i == axis ? _gizmoHoverColor[i] : _gizmoColor[i]);
		}

		_currentAxis = (ResizeGizmoAxis)axis;
		if (RootGizmos != null)
		{
			if (_currentAxis != ResizeGizmoAxis.None)
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
		Transform3D pivot = Targets[0].GetGlobalTransform();

		int column = 0;
		if (_currentAxis == ResizeGizmoAxis.Left || _currentAxis == ResizeGizmoAxis.Right)
		{
			column = 0;
		}
		else if (_currentAxis == ResizeGizmoAxis.Top || _currentAxis == ResizeGizmoAxis.Bottom)
		{
			column = 1;
		}
		else if (_currentAxis == ResizeGizmoAxis.Front || _currentAxis == ResizeGizmoAxis.Back)
		{
			column = 2;
		}

		Vector3 motionMask = pivot.Basis.GetColumn(column).Normalized();
		Plane plane = new(cameraNormal.Normalized(), pivot.Origin);

		Vector3? intersection = plane.IntersectsRay(rayOrigin, rayNormal);
		Vector3? click = plane.IntersectsRay(_startRayOrigin!.Value, _startRayNormal!.Value);

		if (intersection == null || click == null)
			return;

		Vector3 motion = motionMask.Dot(intersection.Value - click.Value) * motionMask;

		Dragged?.Invoke(_currentAxis, motion);
	}
}
