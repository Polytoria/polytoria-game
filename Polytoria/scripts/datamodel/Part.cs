// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Part : Entity
{
	private MeshInstance3D? _mesh;
	private CollisionShape3D _collider = null!;
	private Material _meshMaterial = null!;
	private ShapeEnum _shape;
	private PartMaterialEnum _material;
	private Color _color = new(1, 1, 1);
	private bool _isSeparateMesh = false;
	private bool _castShadows;

	private Node3D _nRemoteAt = null!; // Remote collider proxy

	internal Shape3D ColliderShape => _collider.Shape;

	public bool IsMeshSeparated => _isSeparateMesh;
	public int BridgeID = -1;

	public override void EnterTree()
	{
		Instance? current = Parent;
		while (current != null)
		{
			if (current is UIViewport)
			{
				OverrideNoMultiMesh = true;
				CreateSeparateMesh();
			}
			current = current.Parent;
		}

		base.EnterTree();
	}

	public override void Init()
	{
		base.Init();
		GDNode3D.AddChild(_collider = new(), false, Node.InternalMode.Back);
		GDNode3D.AddChild(_nRemoteAt = new(), false, Node.InternalMode.Back);
		SetRemoteLinkTarget(_collider, _nRemoteAt);
		_nRemoteAt.Rotation = Vector3.Zero;

		if (OS.HasFeature("debug-face"))
		{
			RayCast3D raycast = new()
			{
				TargetPosition = new(0, 0, 2)
			};
			GDNode3D.AddChild(raycast);
		}

		Shape = this is Truss ? ShapeEnum.Truss : ShapeEnum.Brick;
	}

	public override void PreDelete()
	{
		RemoveCollisionShape(_collider);
		base.PreDelete();
	}

	public override void Ready()
	{
		AddCollisionShape(_collider);
		UpdateCollision();
		UpdateMeshSize();
		UpdateShape();

		base.Ready();
	}

	public void CreateSeparateMesh()
	{
		if (_isSeparateMesh)
		{
			return;
		}
		_isSeparateMesh = true;
		if (Root != null && Root.Bridge != null)
		{
			Root.Bridge.SeparatedPartCount++;
		}
		GDNode3D.AddChild(_mesh = new(), false);
		UpdateMeshSize();
		UpdateShape();

		_meshMaterial = Globals.LoadMaterial(_material, Color.A);
		_mesh.MaterialOverride = _meshMaterial;

		UpdateColor();
		UpdateShadow();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		UpdateMeshSize();
		base.OnNodeSizeChanged(newSize);
	}

	private void UpdateMeshSize()
	{
		_mesh?.Scale = NodeSize;
		_nRemoteAt?.Scale = NodeSize;
	}

	public void RemoveSeparateMesh()
	{
		if (!_isSeparateMesh)
		{
			return;
		}
		_isSeparateMesh = false;
		Root.Bridge.SeparatedPartCount--;
		_mesh?.Free();
		_mesh = null;
	}

	[Editable, ScriptProperty, DefaultValue(ShapeEnum.Brick)]
	public ShapeEnum Shape
	{
		get => _shape;
		set
		{
			if (_shape == value)
			{
				return;
			}

			_shape = value;

			UpdateShape();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(PartMaterialEnum.SmoothPlastic)]
	public PartMaterialEnum Material
	{
		get => _material;
		set
		{
			if (_material == value)
			{
				return;
			}

			_material = value;

			UpdateMaterial();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set
		{
			if (_color == value)
			{
				return;
			}

			_color = value;
			//GD.PushWarning("Set color: ", _color);

			UpdateColor();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public override bool CastShadows
	{
		get => _castShadows;
		set
		{
			if (_castShadows == value)
			{
				return;
			}

			_castShadows = value;

			UpdateShadow();
			OnPropertyChanged();
		}
	}

	// Override this to be excluded from MutliMesh
	internal bool OverrideNoMultiMesh = false;

	internal void UpdateShape()
	{
		if (_collider == null) return;
		(Godot.Mesh mesh, Shape3D shape) = Globals.LoadShape(_shape.ToString());
		if (_isSeparateMesh)
		{
			_mesh?.Mesh = mesh;
			_collider.Shape = shape;
		}
		else
		{
			_collider.Shape = shape;
		}
		PostCollisionShapeUpdate(_collider);
	}

	internal void UpdateMaterial()
	{
		if (!_isSeparateMesh || _mesh == null)
		{
			return;
		}

		_meshMaterial = Globals.LoadMaterial(_material, Color.A);
		_mesh.MaterialOverride = _meshMaterial;

		UpdateColor();
	}

	internal void UpdateColor()
	{
		if (_isSeparateMesh && _mesh != null)
		{
			Material targetMat = Globals.LoadMaterial(_material, Color.A);
			if (!ReferenceEquals(_meshMaterial, targetMat))
			{
				_meshMaterial = targetMat;
				_mesh.MaterialOverride = _meshMaterial;
			}

			_mesh.SetInstanceShaderParameter("color", _color);
		}

		UpdateCamLayer();
	}

	internal void UpdateShadow()
	{
		if (_isSeparateMesh)
		{
			_mesh?.CastShadow = _castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		}
	}

	public override Aabb GetSelfBound()
	{
		Transform3D t = GetGlobalTransform();

		Vector3 localSize = Size;
		Vector3 he = localSize / 2f;

		Vector3 basisScale = t.Basis.Scale;

		// get pure rotation matrix
		Basis rot = t.Basis;
		rot.X /= basisScale.X;
		rot.Y /= basisScale.Y;
		rot.Z /= basisScale.Z;

		// some dark magic
		Vector3 worldExtents = new(
			Mathf.Abs(rot.X.X) * he.X + Mathf.Abs(rot.Y.X) * he.Y + Mathf.Abs(rot.Z.X) * he.Z,
			Mathf.Abs(rot.X.Y) * he.X + Mathf.Abs(rot.Y.Y) * he.Y + Mathf.Abs(rot.Z.Y) * he.Z,
			Mathf.Abs(rot.X.Z) * he.X + Mathf.Abs(rot.Y.Z) * he.Y + Mathf.Abs(rot.Z.Z) * he.Z
		);

		Vector3 center = t.Origin;

		return new(center - worldExtents, worldExtents * 2);
	}

	[ScriptEnum("PartShape")]
	public enum ShapeEnum
	{
		Brick = 0,
		Sphere = 1,
		Cylinder = 2,
		Cone = 3,
		Wedge = 4,
		Corner = 5,
		Bevel = 6,
		Concave = 7,
		Truss = 8,
		Frame = 9
	}

	[Attributes.Obsolete("This should not be used, it's here only for compatibility with legacy scripts.")]
	public enum LegacyShapeEnum
	{
		Brick = 0,
		Ball = 1,
		Cylinder = 2,
		Wedge = 4,
		Truss = 8,
		TrussFrame = 9,
		Bevel = 6,
		QuarterPipe = 7,
		Cone = 3,
		CornerWedge = 5,
	}

	[ScriptEnum]
	[CreatorEnumOptions(SortOption = EnumSortOption.Alphabetical)]
	public enum PartMaterialEnum
	{
		SmoothPlastic,
		Brick,
		Concrete,
		Dirt,
		Fabric,
		Grass,
		Ice,
		Marble,
		Metal,
		MetalGrid,
		MetalPlate,
		Neon,
		Planks,
		Plastic,
		Plywood,
		RustyIron,
		Sand,
		Sandstone,
		Snow,
		Stone,
		Wood
	}
}
