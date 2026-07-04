// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Utils;

namespace Polytoria.Creator.Spatial;

public partial class SelectionBox : Node
{
	private Dynamic? _target;
	public Gizmos? RootGizmos { get; set; }
	public World Root = null!;
	public Dynamic? Target
	{
		get => _target;
		set
		{
			GenerateBoxes();
			if (_target != value)
			{
				_target?.TransformChanged -= UpdateBox;
				_target = value;
				// When the target changes drop the cache so the new target recalculates it's bounds.
				InvalidateBoundCache();
				UpdateBox();
				_target?.TransformChanged += UpdateBox;
			}
		}
	}
	public Color SelectionColor = new(1f, 0.5f, 0f);

	private MeshInstance3D _selectionBoxMesh = null!;
	private MeshInstance3D _selectionBoxXrayMesh = null!;

	private ArrayMesh _selectionBox = null!;
	private ArrayMesh _selectionBoxXray = null!;

	private float _gizmoScale;
	private Camera3D _camera = null!;

	private StandardMaterial3D _mat = null!;
	private StandardMaterial3D _matXray = null!;

	private Aabb? _cachedGlobalBounds = null;
	private Vector3 _cachedTargetPosition;

	private bool _boxGenerated = false;

	public override void _EnterTree()
	{
		GenerateBoxes();
		UpdateBox();
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		_selectionBoxMesh?.QueueFree();
		_selectionBoxXrayMesh?.QueueFree();

		_selectionBox?.Dispose();
		_selectionBoxXray?.Dispose();
		_mat?.Dispose();
		_matXray?.Dispose();
		base._ExitTree();
	}

	public override void _Ready()
	{
		_camera = GetViewport().GetCamera3D();
	}

	private void GenerateBoxes()
	{
		if (_boxGenerated) return;
		_boxGenerated = true;
		Aabb aabb = new(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(1, 1, 1));

		SurfaceTool st = new();
		SurfaceTool stXray = new();

		st.Begin(Godot.Mesh.PrimitiveType.Lines);
		stXray.Begin(Godot.Mesh.PrimitiveType.Lines);

		for (int i = 0; i < 12; i++)
		{
			aabb.GetEdge(i, out Vector3 a, out Vector3 b);

			st.AddVertex(a);
			st.AddVertex(b);
			stXray.AddVertex(a);
			stXray.AddVertex(b);
		}

		_mat = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		st.SetMaterial(_mat);
		_selectionBox = st.Commit();

		_matXray = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		stXray.SetMaterial(_matXray);
		_selectionBoxXray = stXray.Commit();

		st.Dispose();
		stXray.Dispose();

		_selectionBoxMesh = new MeshInstance3D { Mesh = _selectionBox };
		Root.GDNode.AddChild(_selectionBoxMesh, @internal: Node.InternalMode.Back);

		_selectionBoxXrayMesh = new MeshInstance3D { Mesh = _selectionBoxXray };
		Root.GDNode.AddChild(_selectionBoxXrayMesh, @internal: Node.InternalMode.Back);
	}

	public void InvalidateBoundCache()
	{
		_cachedGlobalBounds = null;
	}

	public void UpdateBox()
	{
		_selectionBoxMesh.Visible = Target != null;
		_selectionBoxXrayMesh.Visible = Target != null;
		if (Target == null) return;

		var toolMode = CreatorService.Interface.ToolMode;
		Aabb globalBounds;
		bool isDragging = RootGizmos != null && RootGizmos.HoveringGizmos && (toolMode == ToolModeEnum.Move || toolMode == ToolModeEnum.Select);

		if (isDragging && _cachedGlobalBounds.HasValue)
		{
			// Fast path: offset the cached bounds
			Vector3 currentPosition = Target.GetGlobalPosition();
			Vector3 positionDelta = currentPosition - _cachedTargetPosition;

			globalBounds = new Aabb(
				_cachedGlobalBounds.Value.Position + positionDelta,
				_cachedGlobalBounds.Value.Size
			);

			_cachedGlobalBounds = globalBounds;
			_cachedTargetPosition = currentPosition;
		}
		else
		{
			// Full recalculation
			globalBounds = Target.CalculateBounds();
			_cachedGlobalBounds = globalBounds;
			_cachedTargetPosition = Target.GetGlobalPosition();
		}

		Vector3 size = globalBounds.Size + Vector3.One * 0.005f;

		Transform3D boxXform = new(
			Basis.FromScale(size),
			globalBounds.GetCenter()
		);

		_mat.AlbedoColor = SelectionColor;
		_matXray.AlbedoColor = SelectionColor * new Color(1f, 1f, 1f, 0.2f);

		_selectionBoxMesh.GlobalTransform = boxXform;
		_selectionBoxXrayMesh.GlobalTransform = boxXform;
	}
}
