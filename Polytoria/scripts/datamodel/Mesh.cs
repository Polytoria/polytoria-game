// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Scripting;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Mesh : Entity
{
	private MeshAsset? _asset;

	private int _assetID = 0;
	private bool _includeOffset;

	private Node3D _meshContainer = null!; // scaled
	private Node3D? _meshNode = null; // offset only

	private CollisionTypeEnum _collisionType = CollisionTypeEnum.Bounds;
	private bool _playAnimationOnStart;
	private bool _usePartColor;
	private Color _color = new(1, 1, 1);
	private bool _castShadows;
	private AnimationPlayer? _animPlay;
	private readonly List<MeshInstance3D> _meshInstances = [];
	private readonly List<Material> _materials = [];
	private Resource? _prevResource;

	[Editable, ScriptProperty]
	public MeshAsset? Asset
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			if (_meshNode != null && Node.IsInstanceValid(_meshNode))
			{
				_meshNode.QueueFree();
			}
			ClearCollisionShapes();
			_meshNode = null;
			_prevResource = null;
			if (_asset != null)
			{
				Loading = true;
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Asset instead"), CloneIgnore]
	public int AssetID
	{
		get => _assetID;
		set
		{
			_assetID = value;
			CreatePTMeshAsset();
			if (_asset != null)
			{
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IncludeOffset
	{
		get => _includeOffset;
		set
		{
			_includeOffset = value;
			UpdateMeshOffset();
		}
	}

	[Editable, ScriptProperty]
	public CollisionTypeEnum CollisionType
	{
		get => _collisionType;
		set
		{
			_collisionType = value;
			RecalculateCollision();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool PlayAnimationOnStart
	{
		get => _playAnimationOnStart;
		set
		{
			_playAnimationOnStart = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool UsePartColor
	{
		get => _usePartColor;
		set
		{
			_usePartColor = value;
			UpdateColor();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set
		{
			_color = value;
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
			_castShadows = value;
			UpdateShadows();
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public string? CurrentAnimation
	{
		get
		{
			if (_animPlay != null)
			{
				return _animPlay.CurrentAnimation;
			}
			return null;
		}
	}

	[ScriptProperty]
	public bool IsAnimationPlaying
	{
		get
		{
			if (_animPlay != null)
			{
				return _animPlay.IsPlaying();
			}
			return false;
		}
	}

	[ScriptProperty] public bool Loading { get; private set; } = false;

	[ScriptProperty] public PTSignal Loaded { get; private set; } = new();

	[ScriptEnum("MeshCollisionType")]
	public enum CollisionTypeEnum
	{
		Bounds,
		Convex,
		Exact
	}

	public override void Init()
	{
		_meshContainer = new Node3D();
		GDNode3D.AddChild(_meshContainer);

		base.Init();
	}

	public override void InitOverrides()
	{
		Size = Vector3.One * 0.5f;
		base.InitOverrides();
	}

	public override void PreDelete()
	{
		_materials.Clear();
		_meshInstances.Clear();
		base.PreDelete();
	}

	private void CreatePTMeshAsset()
	{
		Asset = New<PTMeshAsset>();
		if (Asset is PTMeshAsset mesh)
		{
			mesh.AssetID = (uint)_assetID;
		}
	}

	private void OnResourceLoaded(Resource resource)
	{
		if (_prevResource == resource) return;
		_prevResource = resource;

		if (resource is PackedScene scene)
		{
			if (!Node.IsInstanceValid(GDNode)) return;
			_animPlay = null;
			if (_meshNode != null && Node.IsInstanceValid(_meshNode))
			{
				_meshNode?.QueueFree();
				_meshNode = null;
			}

			Node3D obj = scene.Instantiate<Node3D>();
			_meshNode = obj;
			_meshNode.Visible = false;
			_animPlay = _meshNode.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

			_meshInstances.Clear();
			_materials.Clear();

			// Set Cull mode
			foreach (Node item in GetDescendantsInternal(_meshNode))
			{
				if (item is MeshInstance3D m3d)
				{
					_meshInstances.Add(m3d);

					// Create duplicate so UsePartColor doesn't override each other
					m3d.Mesh = (Godot.Mesh)m3d.Mesh.Duplicate();

					int surfaceCount = m3d.Mesh.GetSurfaceCount();
					for (int i = 0; i < surfaceCount; i++)
					{
						// Duplicate material, same as above
						Material mat = (Material)m3d.Mesh.SurfaceGetMaterial(i).Duplicate();
						m3d.Mesh.SurfaceSetMaterial(i, mat);
						_materials.Add(mat);
						if (mat is StandardMaterial3D sm3d)
						{
							mat.SetMeta("_origin_clr", sm3d.AlbedoColor);
							mat.SetMeta("_origin_hasalpha", sm3d.Transparency != BaseMaterial3D.TransparencyEnum.Disabled);
						}
					}
				}
			}

			UpdateColor();
			UpdateShadows();

			_meshContainer.AddChild(obj);

			if (PlayAnimationOnStart && _animPlay != null)
			{
				string[] animList = _animPlay.GetAnimationList();
				if (animList.Length > 0)
				{
					PlayAnimation(animList[0]);
				}
			}

			UpdateMeshOffset();

			Loading = false;
			Loaded.Invoke();
		}
	}

	public override void HiddenChanged(bool to)
	{
		_meshNode?.Visible = !to;
		base.HiddenChanged(to);
	}

	[ScriptMethod]
	public void PlayAnimation(string animationName, float speed = 1.0f, bool loop = true)
	{
		if (_animPlay == null) { return; }
		Godot.Animation targetAnim = _animPlay.GetAnimation(animationName);
		if (targetAnim != null)
		{
			targetAnim.LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
			_animPlay.Play(animationName, -1, speed);
		}
	}

	[ScriptLegacyMethod("PlayAnimation")]
	public void LegacyPlayAnimation(string animationName, string _ = "", float speed = 1.0f, bool loop = true)
	{
		PlayAnimation(animationName, speed, loop);
	}

	[ScriptMethod]
	public void StopAnimation(string? animationName = null)
	{
		if (_animPlay == null) { return; }
		if (animationName != null)
		{
			if (_animPlay.CurrentAnimation == animationName)
			{
				_animPlay.Stop(true);
			}
		}
		else
		{
			_animPlay.Stop(true);
		}
	}

	[ScriptMethod]
	public string[] GetAnimations()
	{
		if (_animPlay == null) { return []; }
		return _animPlay.GetAnimationList();
	}

	[ScriptMethod]
	public MeshAnimationInfo[] GetAnimationInfo()
	{
		if (_animPlay == null) { return []; }
		List<MeshAnimationInfo> animInfo = [];

		foreach (string animKey in _animPlay.GetAnimationList())
		{
			Godot.Animation anim = _animPlay.GetAnimation(animKey);
			animInfo.Add(new()
			{
				Name = animKey,
				Length = anim.Length,
				IsPlaying = _animPlay.CurrentAnimation == animKey
			});
		}

		return [.. animInfo];
	}

	private void UpdateColor()
	{
		foreach (Material item in _materials)
		{
			if (item is StandardMaterial3D sm)
			{
				Color origin = sm.GetMeta("_origin_clr").AsColor();
				bool hasAlpha = sm.GetMeta("_origin_hasalpha").AsBool();
				Color setto = _color;
				if (!UsePartColor)
				{
					setto = origin;
				}
				sm.AlbedoColor = setto;
				if (!hasAlpha)
					sm.Transparency = setto.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
			}
		}

		UpdateCamLayer();
	}

	private void UpdateShadows()
	{
		foreach (MeshInstance3D item in _meshInstances)
		{
			item.CastShadow = _castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		}
	}

	private void UpdateMeshOffset()
	{
		if (_meshNode == null)
		{
			return;
		}
		if (IncludeOffset)
		{
			_meshNode.Position = Vector3.Zero;
		}
		else
		{
			_meshNode.ForceUpdateTransform();
			Vector3 offset = -_meshNode.CalculateBounds().GetCenter();

			_meshNode.Position = offset;
		}
		_meshNode.Visible = !IsHidden;

		RecalculateCollision();
	}

	private void RecalculateCollision()
	{
		if (_meshNode == null)
		{
			return;
		}
		// Clear old collisions
		ClearCollisionShapes();

		if (CollisionType == CollisionTypeEnum.Bounds)
		{
			// Create box collision from AABB
			foreach (Node node in GetDescendantsInternal(_meshNode))
			{
				if (node is MeshInstance3D meshInstance)
				{
					Aabb bounds = meshInstance.GetAabb();
					BoxShape3D boxShape = new()
					{
						Size = bounds.Size
					};
					CollisionShape3D collisionShape = new()
					{
						Shape = boxShape,
					};
					GDNode3D.AddChild(collisionShape);

					SetRemoteLinkTarget(collisionShape, meshInstance);
					SetRemoteLinkOffset(collisionShape, meshInstance.GetAabb().GetCenter());
					AddCollisionShape(collisionShape);
				}
			}
		}
		else if (CollisionType == CollisionTypeEnum.Convex || CollisionType == CollisionTypeEnum.Exact)
		{
			foreach (Node node in GetDescendantsInternal(_meshNode))
			{
				if (node is MeshInstance3D meshInstance)
				{
					Godot.Mesh mesh = meshInstance.Mesh;
					Vector3[] facesArray = mesh.GetFaces();
					Shape3D shape;

					if (CollisionType == CollisionTypeEnum.Convex)
					{
						ConvexPolygonShape3D convex = new()
						{
							Points = facesArray
						};
						shape = convex;
					}
					else
					{
						ConcavePolygonShape3D concave = new();
						concave.SetFaces(facesArray);
						shape = concave;
					}

					CollisionShape3D collisionShape = new()
					{
						Shape = shape,
						DebugColor = Color.FromString("#F2821B", new Color())
					};

					GDNode3D.AddChild(collisionShape);
					SetRemoteLinkTarget(collisionShape, meshInstance);
					AddCollisionShape(collisionShape);
				}
			}
		}

		UpdateCollision();
	}

	public override Aabb GetSelfBound()
	{
		Aabb? bounds = null;

		foreach (MeshInstance3D v3d in _meshInstances)
		{

			if (!v3d.IsVisibleInTree())
			{
				continue;
			}

			bool shouldExclude = false;
			foreach (Node3D excludedNode in excludedBoundNodes)
			{
				if (v3d == excludedNode || v3d.IsDescendantOf(excludedNode))
				{
					shouldExclude = true;
					break;
				}
			}

			if (shouldExclude)
			{
				continue;
			}

			if (!v3d.IsInsideTree())
			{
				continue;
			}

			Aabb vBounds = v3d.GlobalTransform * v3d.GetAabb();
			if (vBounds.Size == Vector3.Zero)
			{
				continue;
			}

			if (bounds == null)
			{
				bounds = vBounds;
			}
			else
			{
				bounds = bounds.Value.Merge(vBounds);
			}
		}

		return bounds ?? default;
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_meshContainer.Scale = newSize;
	}

	public struct MeshAnimationInfo : IScriptObject
	{
		[ScriptProperty] public string Name { get; set; }
		[ScriptProperty] public float Length { get; set; }
		[ScriptProperty] public bool IsPlaying { get; set; }

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(Name, Length);
		}
	}
}
