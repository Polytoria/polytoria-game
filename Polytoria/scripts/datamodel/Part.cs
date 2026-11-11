// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Part : Entity
{
	private CollisionShape3D _collider = null!;
	private ShapeEnum _shape;
	private PartMaterialEnum _material;
	private Color _color = new(1, 1, 1);
	private bool _castShadows;

	private Node3D _nRemoteAt = null!; // Remote collider proxy

	internal Shape3D ColliderShape => _collider.Shape;
	public event Action? ShapeChanged;

	public int BridgeID = -1;

	public override void EnterTree()
	{
		Root?.Bridge?.MarkDirty(this);

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
		UpdateMeshSize();
		UpdateShape();
		UpdateCollision();

		base.Ready();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		UpdateMeshSize();
		ShapeChanged?.Invoke();
		base.OnNodeSizeChanged(newSize);
	}

	private void UpdateMeshSize()
	{
		_nRemoteAt?.Scale = NodeSize;
		if (_collider != null && _collider.GetParent() == GDNode3D)
		{
			_collider.Scale = NodeSize;
		}
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

			ShapeChanged?.Invoke();
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

			UpdateCamLayer();
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

			OnPropertyChanged();
		}
	}

	internal bool IsHeld = false;

	internal void UpdateShape()
	{
		if (_collider == null) return;
		(_, Shape3D shape) = Globals.LoadShape(_shape.ToString());
		_collider.Shape = shape;
		PostCollisionShapeUpdate(_collider);
	}

	private Aabb PointsToBound(Vector3[] points, Basis transform)
	{
		Aabb bound = new(Vector3.Zero, Vector3.Zero);

		foreach (Vector3 point in points)
		{
			bound = bound.Expand(transform * point);
		}

		return bound;
	}

	private Vector3 OrthoNonnormalInv(Basis basis, Vector3 vector)
	{
		return new(
			basis.X.Dot(vector) / basis.X.LengthSquared(),
			basis.Y.Dot(vector) / basis.Y.LengthSquared(),
			basis.Z.Dot(vector) / basis.Z.LengthSquared()
		);
	}

	private Vector3 UnitSphereExtent(Vector3 dir)
	{
		return dir.Normalized();
	}

	private Vector3 UnitCylinderExtent(Vector3 dir)
	{
		float sign = dir.Y > 0 ? 1 : -1;
		dir.Y = 0;
		Vector3 pos = dir.Normalized();
		pos.Y = sign;
		return pos;
	}

	private Vector3 UnitBevelExtent(Vector3 dir)
	{
		if (dir.Y < 0 || dir.Z > 0)
		{
			return dir.Sign();
		}
		float sign = dir.X > 0 ? 1 : -1;
		dir.X = 0;
		Vector3 pos = dir.Normalized() * 2;
		pos.X = sign;
		return pos + new Vector3(0, -1, 1);
	}

	private Vector3 UnitOctantExtent(Vector3 dir)
	{
		if (dir.Y < 0 && dir.Z > 0 || dir.X < 0 && dir.Z > 0 || dir.X < 0 && dir.Y < 0)
		{
			return dir.Sign();
		}
		Vector3 pos = UnitSphereExtent(dir);
		if (pos.X < 0) pos.X = 0;
		if (pos.Y < 0) pos.Y = 0;
		if (pos.Z > 0) pos.Z = 0;
		return pos.Normalized() * 2 + new Vector3(-1, -1, 1);
	}

	private float GetSpheroidExtent(Basis transform, Vector3 dir, Vector3 perp, Vector3 perp2) // `transform` is orthogonal by godot's definition
	{
		// adapted from https://www.desmos.com/3d/hjyn4si0gd
		return transform.Xform(
			UnitSphereExtent(OrthoNonnormalInv(transform, dir).Project(
				OrthoNonnormalInv(transform, perp2).Cross(
					OrthoNonnormalInv(transform, perp)
				).Normalized()
			))
		).Dot(dir);
	}

	private float GetCylindroidExtent(Basis transform, Vector3 dir, Vector3 perp, Vector3 perp2)
	{
		return transform.Xform(
			UnitCylinderExtent(OrthoNonnormalInv(transform, dir).Project(
				OrthoNonnormalInv(transform, perp2).Cross(
					OrthoNonnormalInv(transform, perp)
				).Normalized()
			))
		).Dot(dir);
	}

	private float GetBevelExtent(Basis transform, Vector3 dir, Vector3 perp, Vector3 perp2)
	{
		return transform.Xform(
			UnitBevelExtent(OrthoNonnormalInv(transform, dir).Project(
				OrthoNonnormalInv(transform, perp2).Cross(
					OrthoNonnormalInv(transform, perp)
				).Normalized()
			))
		).Dot(dir);
	}

	private float GetOctantExtent(Basis transform, Vector3 dir, Vector3 perp, Vector3 perp2)
	{
		return transform.Xform(
			UnitOctantExtent(OrthoNonnormalInv(transform, dir).Project(
				OrthoNonnormalInv(transform, perp2).Cross(
					OrthoNonnormalInv(transform, perp)
				).Normalized()
			))
		).Dot(dir);
	}

	public override Aabb GetSelfBound()
	{
		Transform3D t = GetGlobalTransform();

		Vector3 localSize = Size;
		Vector3 he = localSize / 2f;

		// get pure rotation matrix
		Basis rot = t.Basis.Orthonormalized();

		Vector3 center = t.Origin;

		Aabb bound;
		switch (Shape)
		{
			case ShapeEnum.Wedge:
				bound = PointsToBound([
					new( 1,-1, 1 ),
					new(-1, 1, 1 ),
					new(-1,-1, 1 ),
					new( 1,-1,-1 ),
					new(-1, 1,-1 ),
					new(-1,-1,-1 ),
				], rot.ScaledLocal(he));
				bound.Position += center;
				return bound;
			case ShapeEnum.Corner:
				bound = PointsToBound([
					new( 1,-1, 1 ),
					new(-1,-1, 1 ),
					new( 1,-1,-1 ),
					new(-1,-1,-1 ),
					new(-1, 1,-1 ),
				], rot.ScaledLocal(he));
				bound.Position += center;
				return bound;
			case ShapeEnum.Concave:
				bound = PointsToBound([
					new( 1,-1,-1 ),
					new( 1, 1, 1 ),
					new( 1,-1, 1 ),
					new(-1,-1,-1 ),
					new(-1, 1, 1 ),
					new(-1,-1, 1 ),
				], rot.ScaledLocal(he));
				bound.Position += center;
				return bound;
			case ShapeEnum.ConcaveCorner:
				bound = PointsToBound([
					new( 1,-1,-1 ),
					new( 1,-1, 1 ),
					new(-1,-1,-1 ),
					new(-1, 1, 1 ),
					new(-1,-1, 1 ),
				], rot.ScaledLocal(he));
				bound.Position += center;
				return bound;
			case ShapeEnum.TriangleCorner:
			case ShapeEnum.TriangleConcaveCorner:
				bound = PointsToBound([
					new( 1,-1, 1 ),
					new(-1,-1,-1 ),
					new(-1, 1, 1 ),
					new(-1,-1, 1 ),
				], rot.ScaledLocal(he));
				bound.Position += center;
				return bound;
			case ShapeEnum.Sphere:
				{
					Basis b = t.Basis;
					Vector3 worldExtents = new(
						GetSpheroidExtent(b, Vector3.Right, Vector3.Up, Vector3.Back),
						GetSpheroidExtent(b, Vector3.Up, Vector3.Back, Vector3.Right),
						GetSpheroidExtent(b, Vector3.Back, Vector3.Right, Vector3.Up)
					);
					return new(center - worldExtents * 0.5f, worldExtents);
				}
			case ShapeEnum.Cylinder:
				{
					Basis b = t.Basis;
					Vector3 worldExtents = new(
						GetCylindroidExtent(b, Vector3.Right, Vector3.Up, Vector3.Back),
						GetCylindroidExtent(b, Vector3.Up, Vector3.Back, Vector3.Right),
						GetCylindroidExtent(b, Vector3.Back, Vector3.Right, Vector3.Up)
					);
					return new(center - worldExtents * 0.5f, worldExtents);
				}
			case ShapeEnum.Cone:
				{
					Basis b = t.Basis;
					Vector3 halfUp = b.Y * 0.5f;
					b = new(b.X, b.Y.Normalized() * 0.001f, b.Z);
					Vector3 worldExtents = new(
						GetSpheroidExtent(b, Vector3.Right, Vector3.Up, Vector3.Back),
						GetSpheroidExtent(b, Vector3.Up, Vector3.Back, Vector3.Right),
						GetSpheroidExtent(b, Vector3.Back, Vector3.Right, Vector3.Up)
					);
					Aabb baseBox = new(center - worldExtents * 0.5f - halfUp, worldExtents);
					return baseBox.Expand(center + halfUp);
				}
			case ShapeEnum.Bevel:
				{
					Basis b = t.Basis;
					Vector3 positiveWorldExtents = new(
						GetBevelExtent(b, Vector3.Right, Vector3.Up, Vector3.Back),
						GetBevelExtent(b, Vector3.Up, Vector3.Back, Vector3.Right),
						GetBevelExtent(b, Vector3.Back, Vector3.Right, Vector3.Up)
					);
					Vector3 negativeWorldExtents = new(
						GetBevelExtent(b, Vector3.Left, Vector3.Up, Vector3.Back),
						GetBevelExtent(b, Vector3.Down, Vector3.Back, Vector3.Right),
						GetBevelExtent(b, Vector3.Forward, Vector3.Right, Vector3.Up)
					);
					return new(center - negativeWorldExtents * 0.5f, (negativeWorldExtents + positiveWorldExtents) * 0.5f);
				}
			case ShapeEnum.Octant:
				{
					Basis b = t.Basis;
					Vector3 positiveWorldExtents = new(
						GetOctantExtent(b, Vector3.Right, Vector3.Up, Vector3.Back),
						GetOctantExtent(b, Vector3.Up, Vector3.Back, Vector3.Right),
						GetOctantExtent(b, Vector3.Back, Vector3.Right, Vector3.Up)
					);
					Vector3 negativeWorldExtents = new(
						GetOctantExtent(b, Vector3.Left, Vector3.Up, Vector3.Back),
						GetOctantExtent(b, Vector3.Down, Vector3.Back, Vector3.Right),
						GetOctantExtent(b, Vector3.Forward, Vector3.Right, Vector3.Up)
					);
					return new(center - negativeWorldExtents * 0.5f, (negativeWorldExtents + positiveWorldExtents) * 0.5f);
				}
			case ShapeEnum.Brick:
			case ShapeEnum.Truss:
			case ShapeEnum.Frame:
			default: // BeveledCorner Torus are currently unimplemented
				{
					Vector3 worldExtents = rot.X.Abs() * he.X + rot.Y.Abs() * he.Y + rot.Z.Abs() * he.Z;
					return new(center - worldExtents, worldExtents * 2);
				}
		}
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
