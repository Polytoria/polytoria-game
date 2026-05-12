// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
namespace Polytoria.Datamodel;

[Instantiable]
public partial class Marker3D : Dynamic
{
	private MeshInstance3D _meshInstance = null!;
	private float _length;
	private bool _appearOnTop;
	private bool _visibleInDev;

	[Editable, ScriptProperty, DefaultValue(1)]
	public float Length
	{
		get => _length;
		set
		{
			_length = value;
			RenderGizmo();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool AppearOnTop
	{
		get => _appearOnTop;
		set
		{
			_appearOnTop = value;
			RenderGizmo();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool VisibleInDev
	{
		get => _visibleInDev;
		set
		{
			_visibleInDev = value;
#if CREATOR
			_meshInstance.Visible = _visibleInDev;
#else
			_meshInstance.Visible = false;
#endif
			RenderGizmo();
		}
	}

	public override void Init()
	{
		_meshInstance = new MeshInstance3D();
		GDNode.AddChild(_meshInstance, @internal: Node.InternalMode.Back);
		RenderGizmo();
		base.Init();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_meshInstance.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	private void RenderGizmo()
	{
		if (_meshInstance == null)
			return;
		ArrayMesh mesh = new();
		float leng = Length;

		// X-axis
		SurfaceTool stX = new();
		stX.Begin(Godot.Mesh.PrimitiveType.Lines);
		StandardMaterial3D matX = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1, 0, 0), // Red
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = AppearOnTop
		};
		stX.SetMaterial(matX);
		stX.AddVertex(Vector3.Zero);
		stX.AddVertex(new Vector3(leng, 0, 0));
		stX.Commit(mesh);

		// Y-axis
		SurfaceTool stY = new();
		stY.Begin(Godot.Mesh.PrimitiveType.Lines);
		StandardMaterial3D matY = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0, 1, 0), // Green
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = AppearOnTop
		};
		stY.SetMaterial(matY);
		stY.AddVertex(Vector3.Zero);
		stY.AddVertex(new Vector3(0, leng, 0));
		stY.Commit(mesh);

		// Z-axis
		SurfaceTool stZ = new();
		stZ.Begin(Godot.Mesh.PrimitiveType.Lines);
		StandardMaterial3D matZ = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0, 0, 1), // Blue
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = AppearOnTop
		};
		stZ.SetMaterial(matZ);
		stZ.AddVertex(Vector3.Zero);
		stZ.AddVertex(new Vector3(0, 0, leng));
		stZ.Commit(mesh);

		_meshInstance.Mesh = mesh;
		_meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
}
