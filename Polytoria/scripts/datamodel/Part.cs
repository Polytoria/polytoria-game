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
	private MultiMeshInstance3D? _trussSeparateMesh;
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

	private float _lastTrussCrossSection = -1f;
	private float TrussCrossSection => NodeSize.X;
	internal bool OverrideNoMultiMesh = false;

	private const float MinTrussScale = 0.0001f;
	internal const float TrussRingBandFraction = 0.1f;

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

		if (Shape is ShapeEnum.Truss or ShapeEnum.Frame)
		{
			(Godot.Mesh mesh, _) = Globals.LoadShape(_shape.ToString());
			MultiMesh mm = new()
			{
				Mesh = mesh,
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true
			};
			GDNode3D.AddChild(_trussSeparateMesh = new() { Multimesh = mm }, false);
			_meshMaterial = Globals.LoadMaterial(_material, Color.A);
			_trussSeparateMesh.MaterialOverride = _meshMaterial;
			UpdateShadow();
			RefreshSeparateTrussMesh();
			return;
		}

		GDNode3D.AddChild(_mesh = new(), false);
		UpdateMeshSize();
		UpdateShape();

		_meshMaterial = Globals.LoadMaterial(_material, Color.A);
		_mesh.MaterialOverride = _meshMaterial;

		UpdateColor();
		UpdateShadow();
	}

	// Pushes GetLocalTrussInstances() into the separate mesh's own private
	// multimesh. Local-space so it follows this part's own transform via
	// normal scene-tree parenting, no per-frame recompute needed.
	private void RefreshSeparateTrussMesh()
	{
		if (_trussSeparateMesh == null) return;

		Transform3D[] instances = GetLocalTrussInstances();
		MultiMesh mm = _trussSeparateMesh.Multimesh;
		mm.InstanceCount = Mathf.Max(1, instances.Length);
		mm.VisibleInstanceCount = instances.Length;

		Color linearColor = _color.SrgbToLinear();
		for (int i = 0; i < instances.Length; i++)
		{
			mm.SetInstanceTransform(i, instances[i]);
			mm.SetInstanceColor(i, linearColor);
		}
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (Shape is ShapeEnum.Truss or ShapeEnum.Frame)
		{
			EnforceTrussCrossSectionSquare(newSize);
		}
		UpdateMeshSize();
		RefreshSeparateTrussMesh();
		base.OnNodeSizeChanged(newSize);
	}

	private void EnforceTrussCrossSectionSquare(Vector3 newSize)
	{
		float x = newSize.X;
		float z = newSize.Z;

		if (Mathf.IsEqualApprox(x, z))
		{
			_lastTrussCrossSection = x;
			EnforceTrussMinLength();
			return;
		}

		bool xChanged = !Mathf.IsEqualApprox(x, _lastTrussCrossSection);
		float cross = xChanged ? x : z;

		NodeSize = new Vector3(cross, newSize.Y, cross);
		_lastTrussCrossSection = cross;
		EnforceTrussMinLength();
	}

	// Y can't go below one segment's own length, or no valid segment fits.
	private void EnforceTrussMinLength()
	{
		if (NodeSize.Y >= NodeSize.X) return;
		NodeSize = new Vector3(NodeSize.X, NodeSize.X, NodeSize.Z);
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
		_trussSeparateMesh?.Free();
		_trussSeparateMesh = null;
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

	internal virtual Transform3D[] GetBatchInstances()
	{
		if (Shape is not (ShapeEnum.Truss or ShapeEnum.Frame))
		{
			return [GetGlobalTransform()];
		}

		TrussSegment[]? segments = BuildTrussSegments();
		if (segments == null) return [GetGlobalTransform()];

		Transform3D transform = GDNode3D.GlobalTransform;

		return BuildInstancesFrom(transform.Origin, transform.Basis, segments);
	}

	private Transform3D[] GetLocalTrussInstances()
	{
		TrussSegment[]? segments = BuildTrussSegments();
		if (segments == null) return [];

		return BuildInstancesFrom(Vector3.Zero, Basis.Identity, segments);
	}

	private Transform3D[] BuildInstancesFrom(Vector3 origin, Basis rotationBasis, TrussSegment[] segments)
	{
		float crossSection = TrussCrossSection;
		var result = new Transform3D[segments.Length];
		for (int i = 0; i < segments.Length; i++)
		{
			Basis basis = new(rotationBasis.X * crossSection, rotationBasis.Y * segments[i].SegLen, rotationBasis.Z * crossSection);
			result[i] = new Transform3D(basis, origin + rotationBasis.Y * segments[i].OffsetY);
		}
		return result;
	}

	private readonly struct TrussSegment(float offsetY, float segLen)
	{
		public readonly float OffsetY = offsetY;
		public readonly float SegLen = segLen;
	}

	private TrussSegment[]? BuildTrussSegments()
	{
		float crossSection = TrussCrossSection;
		float length = NodeSize.Y;
		if (crossSection <= MinTrussScale || length <= 0f) return null;

		// Round to nearest cell count so N stretched cells fill `length` exactly.
		int N = Mathf.Max(1, Mathf.RoundToInt(length / crossSection));

		float denom = N - (N - 1) * TrussRingBandFraction;
		float segLen = length / Mathf.Max(denom, MinTrussScale);

		// Adjacent segment centres are spaced by the non-overlapping visible length.
		float stepY = segLen * (1f - TrussRingBandFraction);

		TrussSegment[] result = new TrussSegment[N];
		for (int i = 0; i < N; i++)
		{
			float offsetY = -length / 2f + segLen / 2f + i * stepY;
			result[i] = new TrussSegment(offsetY, segLen);
		}
		return result;
	}

	private void NormalizeTrussOnShapeSet()
	{
		float cross = Mathf.Min(NodeSize.X, NodeSize.Z);
		float y = Mathf.Max(NodeSize.Y, cross);
		NodeSize = new Vector3(cross, y, cross);
		_lastTrussCrossSection = cross;
	}

	internal void UpdateShape()
	{
		if (_collider == null) return;

		if (_shape is ShapeEnum.Truss or ShapeEnum.Frame)
		{
			NormalizeTrussOnShapeSet();
		}

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
		if (!_isSeparateMesh) return;

		if (_trussSeparateMesh != null)
		{
			_meshMaterial = Globals.LoadMaterial(_material, Color.A);
			_trussSeparateMesh.MaterialOverride = _meshMaterial;
			UpdateColor();
			return;
		}

		if (_mesh == null)
		{
			return;
		}

		_meshMaterial = Globals.LoadMaterial(_material, Color.A);
		_mesh.MaterialOverride = _meshMaterial;

		UpdateColor();
	}

	internal void UpdateColor()
	{
		if (_trussSeparateMesh != null)
		{
			RefreshSeparateTrussMesh();
			UpdateCamLayer();
			return;
		}

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
		if (_trussSeparateMesh != null)
		{
			_trussSeparateMesh.CastShadow = _castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
			return;
		}
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
		Frame = 9,
		Octant = 10,
		Torus = 11,
		BeveledCorner = 12,
		ConcaveCorner = 13,
		TriangleCorner = 14,
		TriangleConcaveCorner = 15
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
