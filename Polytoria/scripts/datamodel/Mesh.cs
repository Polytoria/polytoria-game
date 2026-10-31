// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
	private TextureFilterEnum _textureFilter;
	private bool _playAnimationOnStart;
	private bool _usePartColor;
	private Color _color = new(1, 1, 1);
	private bool _castShadows;
	private AnimationPlayer? _animPlay;
	private readonly List<MeshInstance3D> _meshInstances = [];
	private readonly List<SurfaceMaterial> _materials = [];
	private Resource? _prevResource;

	private static readonly ConditionalWeakTable<Godot.Mesh, Shape3D> _convexShapesCache = [];
	private static readonly ConditionalWeakTable<Godot.Mesh, Shape3D> _concaveShapesCache = [];
	private static readonly ConditionalWeakTable<StandardMaterial3D, (StringName Name, Variant Value)[]> _materialPropsCache = [];

	[Editable, ScriptProperty]
	public MeshAsset? Asset
	{
		get => _asset;
		set
		{
			if (_asset != null)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.PropertyChanged.Disconnect(UpdateColor);
				if (_asset != value)
				{
					_asset.UnlinkFrom(this);
				}
			}
			_asset = value;
			if (_meshNode != null && Node.IsInstanceValid(_meshNode))
			{
				_meshNode.QueueFree();
			}
			ClearCollisionShapes();
			_meshNode = null;
			_materials.Clear();
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
			_asset?.PropertyChanged.Connect(UpdateColor);
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

	[Editable, ScriptProperty, DefaultValue(TextureFilterEnum.Linear)]
	public TextureFilterEnum TextureFilter
	{
		get => _textureFilter;
		set
		{
			_textureFilter = value;
			UpdateMaterials();
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

	[SyncVar] public string? ServerAnim { get; internal set; }
	[SyncVar] public float ServerAnimSpeed { get; internal set; } = 1.0f;
	[SyncVar] public double ServerAnimStart { get; internal set; }

	private double ServerTime => Root?.Network != null ? (double)Root.ServerTime : 0;
	private bool CanSyncAnim => Root?.Network != null && (Root.Network.IsServer || (HasAuthority && ExistInNetwork));

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

	public override void Ready()
	{
		if (ServerAnim != null)
		{
			InternalPlayAnimation(ServerAnim, ServerAnimSpeed, true, ServerAnimStart);
		}
		base.Ready();
	}

	public override void InitOverrides()
	{
		Size = Vector3.One * 0.5f;
		base.InitOverrides();
	}

	public override void PreDelete()
	{
		_asset?.PropertyChanged.Disconnect(UpdateColor);
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

	private static BaseMaterial3D.TextureFilterEnum ToGodotTextureFilter(TextureFilterEnum filter)
	{
		return filter switch
		{
			TextureFilterEnum.Nearest => BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
			TextureFilterEnum.NearestNoMipmaps => BaseMaterial3D.TextureFilterEnum.Nearest,
			TextureFilterEnum.Linear => BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			TextureFilterEnum.LinearNoMipmaps => BaseMaterial3D.TextureFilterEnum.Linear,
			_ => BaseMaterial3D.TextureFilterEnum.Linear,
		};
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

			if (_animPlay != null)
			{
				foreach (StringName libName in _animPlay.GetAnimationLibraryList())
				{
					AnimationLibrary lib = _animPlay.GetAnimationLibrary(libName);
					AnimationLibrary newLib = new();
					foreach (StringName animKey in lib.GetAnimationList())
					{
						newLib.AddAnimation(animKey, (Godot.Animation)lib.GetAnimation(animKey).Duplicate());
					}
					_animPlay.RemoveAnimationLibrary(libName);
					_animPlay.AddAnimationLibrary(libName, newLib);
				}
			}

			_meshInstances.Clear();
			_materials.Clear();

			// Set Cull mode
			foreach (Node item in GetDescendantsInternal(_meshNode))
			{
				if (item is MeshInstance3D m3d)
				{
					_meshInstances.Add(m3d);

					Godot.Mesh mesh = m3d.Mesh;
					int surfaceCount = mesh.GetSurfaceCount();
					for (int i = 0; i < surfaceCount; i++)
					{
						if (mesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
						{
							_materials.Add(new(m3d, i, mat));
						}
					}
				}
			}

			UpdateColor();
			UpdateShadows();

			_meshContainer.AddChild(obj);

			if (ServerAnim != null)
			{
				InternalPlayAnimation(ServerAnim, ServerAnimSpeed, true, ServerAnimStart);
			}
			else if (PlayAnimationOnStart && _animPlay != null)
			{
				string[] animList = _animPlay.GetAnimationList();
				if (animList.Length > 0)
				{
					InternalPlayAnimation(animList[0], 1.0f, true, 0);
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
		if (CanSyncAnim)
		{
			Rpc(nameof(NetPlayAnimation), animationName, speed, loop, ServerTime);
		}
		else
		{
			InternalPlayAnimation(animationName, speed, loop, ServerTime);
		}
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetPlayAnimation(string animationName, float speed, bool loop, double startTime)
	{
		ServerAnim = loop ? animationName : null;
		ServerAnimSpeed = speed;
		ServerAnimStart = startTime;
		InternalPlayAnimation(animationName, speed, loop, startTime);
	}

	private void InternalPlayAnimation(string animationName, float speed, bool loop, double startTime)
	{
		if (_animPlay == null) { return; }
		if (!_animPlay.HasAnimation(animationName)) { return; }
		Godot.Animation targetAnim = _animPlay.GetAnimation(animationName);
		targetAnim.LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
		_animPlay.Play(animationName, -1, speed);

		double pos = (ServerTime - startTime) * speed;
		if (loop) pos %= targetAnim.Length;
		if (pos >= 0 && pos < targetAnim.Length)
		{
			_animPlay.Seek(pos, true);
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
		if (CanSyncAnim)
		{
			Rpc(nameof(NetStopAnimation), animationName ?? "");
		}
		else
		{
			InternalStopAnimation(animationName);
		}
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetStopAnimation(string animationName)
	{
		if (animationName == "" || animationName == ServerAnim) ServerAnim = null;
		InternalStopAnimation(animationName);
	}

	private void InternalStopAnimation(string? animationName)
	{
		if (_animPlay == null) { return; }
		if (string.IsNullOrEmpty(animationName) || _animPlay.CurrentAnimation == animationName)
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
		UpdateMaterials();
		UpdateCamLayer();
	}

	private void UpdateMaterials()
	{
		Texture2D? assetTexture = Asset is PTMeshAsset ptma && ptma.Texture?.Resource is Texture2D t2d ? t2d : null;
		BaseMaterial3D.TextureFilterEnum godotFilter = ToGodotTextureFilter(_textureFilter);

		foreach (SurfaceMaterial item in _materials)
		{
			MaterialState original = item.OriginalState;
			Color color = UsePartColor ? _color : original.Color;

			BaseMaterial3D.TransparencyEnum transparency = original.Transparency;
			if (transparency == BaseMaterial3D.TransparencyEnum.Disabled)
				transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;

			item.Apply(new(color, assetTexture ?? original.Texture, transparency, godotFilter));
		}
	}

	private static StandardMaterial3D DuplicateMaterial(StandardMaterial3D source)
	{
		StandardMaterial3D copy = new();
		foreach ((StringName name, Variant value) in _materialPropsCache.GetValue(source, GetNonDefaultProps))
		{
			copy.Set(name, value);
		}
		return copy;
	}

	private static (StringName Name, Variant Value)[] GetNonDefaultProps(StandardMaterial3D source)
	{
		using StandardMaterial3D defaults = new();
		List<(StringName Name, Variant Value)> properties = [];

		foreach (Godot.Collections.Dictionary property in source.GetPropertyList())
		{
			if ((property["usage"].As<PropertyUsageFlags>() & PropertyUsageFlags.Storage) == 0)
			{
				continue;
			}

			StringName name = property["name"].AsStringName();
			Variant value = source.Get(name);
			Variant defaultValue = defaults.Get(name);
			if (value.VariantType != defaultValue.VariantType || !Equals(value.Obj, defaultValue.Obj))
			{
				properties.Add((name, value));
			}
		}

		return [.. properties];
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
			foreach (MeshInstance3D meshInstance in _meshInstances)
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
				SetRemoteLinkOffset(collisionShape, bounds.GetCenter());
				AddCollisionShape(collisionShape);
			}
		}
		else if (CollisionType == CollisionTypeEnum.Convex || CollisionType == CollisionTypeEnum.Exact)
		{
			foreach (MeshInstance3D meshInstance in _meshInstances)
			{
				CollisionShape3D collisionShape = new()
				{
					Shape = LoadCollisionShape(meshInstance.Mesh, CollisionType),
					DebugColor = Color.FromString("#F2821B", new Color())
				};

				GDNode3D.AddChild(collisionShape);
				SetRemoteLinkTarget(collisionShape, meshInstance);
				AddCollisionShape(collisionShape);
			}
		}

		UpdateCollision();
	}

	private static Shape3D LoadCollisionShape(Godot.Mesh mesh, CollisionTypeEnum collisionType)
	{
		ConditionalWeakTable<Godot.Mesh, Shape3D> cache = collisionType == CollisionTypeEnum.Convex ? _convexShapesCache : _concaveShapesCache;
		if (cache.TryGetValue(mesh, out Shape3D? shape))
		{
			return shape;
		}

		Vector3[] facesArray = mesh.GetFaces();
		if (collisionType == CollisionTypeEnum.Convex)
		{
			shape = new ConvexPolygonShape3D() { Points = facesArray };
		}
		else
		{
			ConcavePolygonShape3D concave = new();
			concave.SetFaces(facesArray);
			shape = concave;
		}

		cache.AddOrUpdate(mesh, shape);
		return shape;
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
		[ScriptProperty] public double Length { get; set; }
		[ScriptProperty] public bool IsPlaying { get; set; }

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(Name, Length);
		}
	}

	private readonly record struct MaterialState(Color Color, Texture2D? Texture, BaseMaterial3D.TransparencyEnum Transparency, BaseMaterial3D.TextureFilterEnum TextureFilter);

	private sealed class SurfaceMaterial
	{
		private readonly MeshInstance3D _meshInstance;
		private readonly int _surface;
		private readonly StandardMaterial3D _material;
		private MaterialState _state;
		private bool _isOverridden;

		public SurfaceMaterial(MeshInstance3D meshInstance, int surface, StandardMaterial3D source)
		{
			_meshInstance = meshInstance;
			_surface = surface;
			_material = DuplicateMaterial(source);
			OriginalState = new(source.AlbedoColor, source.AlbedoTexture, source.Transparency, source.TextureFilter);
			_state = OriginalState;
		}

		public MaterialState OriginalState { get; }

		public void Apply(MaterialState state)
		{
			if (state == OriginalState)
			{
				if (_isOverridden)
				{
					_meshInstance.SetSurfaceOverrideMaterial(_surface, null);
					_isOverridden = false;
				}
				return;
			}

			if (state.Texture != _state.Texture) _material.AlbedoTexture = state.Texture;
			if (state.Color != _state.Color) _material.AlbedoColor = state.Color;
			if (state.Transparency != _state.Transparency) _material.Transparency = state.Transparency;
			if (state.TextureFilter != _state.TextureFilter) _material.TextureFilter = state.TextureFilter;
			_state = state;

			if (!_isOverridden)
			{
				_meshInstance.SetSurfaceOverrideMaterial(_surface, _material);
				_isOverridden = true;
			}
		}
	}
}
