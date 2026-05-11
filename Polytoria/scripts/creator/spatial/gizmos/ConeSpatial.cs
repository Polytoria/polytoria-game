// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
namespace Polytoria.Creator.Spatial;

public partial class ConeSpatial : Node3D
{
	private MeshInstance3D _meshInstance = null!;
	private float _range = 30;
	private float _angle = 30;

	public float Range
	{
		get => _range;
		set
		{
			_range = value;
			RenderGizmo();
		}
	}

	public float Angle
	{
		get => _angle;
		set
		{
			_angle = value;
			RenderGizmo();
		}
	}

	public float Segments { get; set; } = 32;

	[Export]
	public Color GizmoColor { get; set; } = new(1f, 0.5f, 0f);

	public override void _Ready()
	{
		if (Globals.CurrentAppEntry != Globals.AppEntryEnum.Creator) { Visible = false; return; }
		_meshInstance = new MeshInstance3D();
		AddChild(_meshInstance);
		RenderGizmo();
	}

	private void RenderGizmo()
	{
		if (_meshInstance == null)
			return;

		float radAngle = Mathf.DegToRad(Angle);
		float baseRadius = Range * Mathf.Tan(radAngle);

		SurfaceTool st = new();
		st.Begin(Mesh.PrimitiveType.Lines);

		for (int i = 0; i < Segments; i++)
		{
			float a1 = 2 * Mathf.Pi * i / Segments;
			float a2 = 2 * Mathf.Pi * (i + 1) / Segments;

			Vector3 p1 = new(baseRadius * Mathf.Cos(a1), baseRadius * Mathf.Sin(a1), Range);
			Vector3 p2 = new(baseRadius * Mathf.Cos(a2), baseRadius * Mathf.Sin(a2), Range);

			st.AddVertex(p1);
			st.AddVertex(p2);
		}

		int edgeCount = 4;
		for (int i = 0; i < edgeCount; i++)
		{
			float a = 2 * Mathf.Pi * i / edgeCount;
			Vector3 basePoint = new(baseRadius * Mathf.Cos(a), baseRadius * Mathf.Sin(a), Range);

			st.AddVertex(Vector3.Zero);
			st.AddVertex(basePoint);
		}

		StandardMaterial3D mat = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = GizmoColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};

		st.SetMaterial(mat);
		_meshInstance.Mesh = st.Commit();
		_meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
}
