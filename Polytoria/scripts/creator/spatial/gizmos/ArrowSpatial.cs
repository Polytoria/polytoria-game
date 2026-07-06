// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
namespace Polytoria.Creator.Spatial;

public partial class ArrowSpatial : Node3D
{
	private MeshInstance3D _meshInstance = null!;
	private float _length = 3;

	public float Length
	{
		get => _length;
		set
		{
			_length = value;
			RenderGizmo();
		}
	}

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

		SurfaceTool st = new();
		st.Begin(Mesh.PrimitiveType.Lines);

		float leng = Length - 1;

		// X Axis
		st.AddVertex(new Vector3(-1f, 0, 0));
		st.AddVertex(new Vector3(-1f, 0, leng));
		st.AddVertex(new Vector3(-1f, 0, leng));
		st.AddVertex(new Vector3(-2f, 0, leng));
		st.AddVertex(new Vector3(-2f, 0, leng));
		st.AddVertex(new Vector3(0f, 0, leng + 2));
		st.AddVertex(new Vector3(0f, 0, leng + 2));
		st.AddVertex(new Vector3(2f, 0, leng));
		st.AddVertex(new Vector3(2f, 0, leng));
		st.AddVertex(new Vector3(1f, 0, leng));
		st.AddVertex(new Vector3(1f, 0, leng));
		st.AddVertex(new Vector3(1f, 0, 0));
		st.AddVertex(new Vector3(1f, 0, 0));
		st.AddVertex(new Vector3(-1f, 0, 0));

		// Y Axis
		st.AddVertex(new Vector3(0f, -1, 0));
		st.AddVertex(new Vector3(0f, 1f, 0));
		st.AddVertex(new Vector3(0f, 1f, 0));
		st.AddVertex(new Vector3(0f, 1f, leng));
		st.AddVertex(new Vector3(0f, 1f, leng));
		st.AddVertex(new Vector3(0f, 2f, leng));
		st.AddVertex(new Vector3(0f, 2f, leng));
		st.AddVertex(new Vector3(0f, 0f, leng + 2));
		st.AddVertex(new Vector3(0f, 0f, leng + 2));
		st.AddVertex(new Vector3(0f, -2f, leng));
		st.AddVertex(new Vector3(0f, -2f, leng));
		st.AddVertex(new Vector3(0f, -1f, leng));
		st.AddVertex(new Vector3(0f, -1f, leng));
		st.AddVertex(new Vector3(0f, -1f, 0));

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
