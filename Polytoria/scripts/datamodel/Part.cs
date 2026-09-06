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
	private bool _hasFaceTextures;
	private ShaderMaterial? _faceTextureOverlay;
	private Vector3 _faceTextureSize;
	private static Shader? _faceTextureShader;

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

		if (_hasFaceTextures)
		{
			RefreshFaceTextures();
		}
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		UpdateMeshSize();
		if (_hasFaceTextures && newSize != _faceTextureSize)
		{
			_faceTextureSize = newSize;
			_faceTextureOverlay?.SetShaderParameter("part_size", newSize);
		}
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

	internal void RefreshFaceTextures(PartTexture? include = null, PartTexture? exclude = null)
	{
		PartTexture?[] slots = new PartTexture?[6];
		bool any = false;

		void addSlot(PartTexture texture)
		{
			if (texture.IsHidden || texture.IsDeleted || texture == exclude)
			{
				return;
			}
			slots[(int)texture.Face] = texture;
			any = true;
		}

		foreach (Instance child in GetChildren())
		{
			if (child is PartTexture texture && texture != include)
			{
				addSlot(texture);
			}
		}

		if (include != null)
		{
			addSlot(include);
		}

		if (any && !_hasFaceTextures)
		{
			_hasFaceTextures = true;
			OverrideNoMultiMesh = true;
			if (Root != null && Root.Bridge != null)
			{
				Root.Bridge.RemovePart(this);
			}
			else
			{
				CreateSeparateMesh();
			}
		}
		else if (!any && _hasFaceTextures)
		{
			_hasFaceTextures = false;
			OverrideNoMultiMesh = IsDescendantOfClass<UIViewport>();
			if (_mesh != null)
			{
				_mesh.MaterialOverlay = null;
			}
			_faceTextureOverlay = null;
			if (!IsDeleted && !OverrideNoMultiMesh && Root != null && Root.Bridge != null)
			{
				Root.Bridge.AddPart(this);
			}
			return;
		}

		if (!any || _mesh == null)
		{
			return;
		}

		_faceTextureShader ??= ResourceLoader.Load<Shader>("res://resources/shaders/part/part_face_texture.gdshader");
		_faceTextureOverlay ??= new ShaderMaterial { Shader = _faceTextureShader };
		_mesh.MaterialOverlay = _faceTextureOverlay;

		_faceTextureSize = Size;
		_faceTextureOverlay.SetShaderParameter("part_size", _faceTextureSize);

		for (int i = 0; i < slots.Length; i++)
		{
			PartTexture? slot = slots[i];
			Texture2D? texture = slot?.LoadedTexture;
			_faceTextureOverlay.SetShaderParameter($"face_texture_{i}", texture);
			_faceTextureOverlay.SetShaderParameter($"face_adjust_{i}", slot == null
				? new Vector4(0f, 1f, 1f, 0f)
				: new Vector4(slot.Exposure, slot.Contrast, slot.Saturation, slot.Temperature));
			_faceTextureOverlay.SetShaderParameter($"face_state_{i}", new Vector2(texture != null ? 1f : 0f, slot?.Transparency ?? 0f));

			Vector4 tile = Vector4.Zero;
			if (slot != null && slot.Mode == PartTexture.TextureModeEnum.Tile && slot.TileSize.X > 0f && slot.TileSize.Y > 0f)
			{
				tile = new Vector4(slot.TileSize.X, slot.TileSize.Y, slot.Offset.X, slot.Offset.Y);
			}
			_faceTextureOverlay.SetShaderParameter($"face_tile_{i}", tile);
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
