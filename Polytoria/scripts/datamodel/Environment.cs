// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Static("Environment")]
public sealed partial class Environment : Instance
{
	private const int MaxOverlaps = 2048;

	// Array of spawnpioints
	public List<Entity> SpawnPoints = [];

	private Vector3 _gravity = new(0, -85, 0);
	private Camera? _currentCamera;
	private Camera3D? _cameraOverride;
	private bool _navBaking = false;
	private readonly List<Node3D> _navTemps = [];

	internal Camera3D? CurrentGDCamera;

	[ScriptProperty]
	public Camera? CurrentCamera
	{
		get
		{
			if (_currentCamera != null && _currentCamera.IsDeleted)
			{
				_currentCamera = null;
			}
			return _currentCamera;
		}
		set
		{
			_currentCamera = value;
			EnforceCamera();
		}
	}

	internal Camera3D? CameraOverride
	{
		get => _cameraOverride;
		set
		{
			_cameraOverride = value;
			EnforceCamera();
		}
	}

	public int PartCount = 0;

	private float _partDestroyHeight;
	private bool _autoGenerateNavMesh;
	private Lighting.SkyboxEnum _skybox = Lighting.SkyboxEnum.Day1;
	private bool _fogEnabled = false;
	private Color _fogColor = new(1, 1, 1);
	private float _fogStartDistance = 0;
	private float _fogEndDistance = 250;

	private NavigationRegion3D _navRegion = null!;
	private NavigationMesh _navMesh = null!;

	[Editable, ScriptProperty]
	public Vector3 Gravity
	{
		get => _gravity;
		set
		{
			_gravity = value;

			Rid space = Root.World3D.Space;
			PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.Gravity, -(_gravity.Y / 5));
			PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.GravityVector, _gravity.Normalized());

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(-2000f)]
	public float PartDestroyHeight
	{
		get => _partDestroyHeight;
		set
		{
			_partDestroyHeight = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool AutoGenerateNavMesh
	{
		get => _autoGenerateNavMesh;
		set
		{
			_autoGenerateNavMesh = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Replaced with Lighting.Skybox")]
	public Lighting.SkyboxEnum Skybox
	{
		get => _skybox;
		set
		{
			_skybox = value;
			Lighting.Skybox = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Replaced with Lighting.FogEnabled")]
	public bool FogEnabled
	{
		get => _fogEnabled;
		set
		{
			_fogEnabled = value;
			Lighting.FogEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Replaced with Lighting.FogColor")]
	public Color FogColor
	{
		get => _fogColor;
		set
		{
			_fogColor = value;
			Lighting.FogColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Replaced with Lighting.FogStartDistance")]
	public float FogStartDistance
	{
		get => _fogStartDistance;
		set
		{
			_fogStartDistance = value;
			Lighting.FogStartDistance = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Attributes.Obsolete("Replaced with Lighting.FogEndDistance")]
	public float FogEndDistance
	{
		get => _fogEndDistance;
		set
		{
			_fogEndDistance = value;
			Lighting.FogEndDistance = value;
			OnPropertyChanged();
		}
	}

	private Lighting Lighting
	{
		get
		{
			Lighting lighting = Root.Lighting;

			if (lighting == null)
			{
				lighting = Globals.LoadInstance<Lighting>(Root);
				lighting.Parent = Root;
			}

			return lighting;
		}
	}

	internal void EnforceCamera()
	{
		if (CameraOverride != null)
		{
			CameraOverride.MakeCurrent();
			CurrentGDCamera = CameraOverride;
		}
		else
		{
			CurrentCamera?.Camera3D?.MakeCurrent();
			CurrentGDCamera = CurrentCamera?.Camera3D;
		}
	}

	public override void Init()
	{
		_navRegion = new();
		GDNode.AddChild(_navRegion, false, Node.InternalMode.Front);
		_navRegion.BakeFinished += OnNavMeshBaked;
		if (Root.IsLoaded)
		{
			OnReady();
		}
		else
		{
			Root.Loaded.Once(OnReady);
		}
		base.Init();
	}

	public override void PreDelete()
	{
		_navRegion.BakeFinished -= OnNavMeshBaked;
		SpawnPoints.Clear();
		_navTemps.Clear();
		base.PreDelete();
	}

	private void OnReady()
	{
		if (AutoGenerateNavMesh)
		{
			RebuildNavMesh();
		}
	}

	private void OnNavMeshBaked()
	{
		foreach (var item in _navTemps)
		{
			item.QueueFree();
		}

		_navTemps.Clear();
		_navBaking = false;
	}


	public void RegisterSpawnPoint(Entity spawnpoint)
	{
		SpawnPoints.Add(spawnpoint);
	}

	public void UnregisterSpawnPoint(Entity spawnpoint)
	{
		SpawnPoints.Remove(spawnpoint);
	}

	[ScriptMethod]
	public RayResult? Raycast(Vector3 origin, Vector3 direction, float maxDistance = 10000f, Instance[]? ignoreList = null)
	{
		PhysicsDirectSpaceState3D spaceState = Root.World3D.DirectSpaceState;

		PhysicsRayQueryParameters3D query = new()
		{
			From = origin,
			To = origin + direction.Normalized() * maxDistance,
			CollideWithAreas = true,
			CollideWithBodies = true
		};

		if (ignoreList != null)
		{
			query.Exclude = PhysicalsToArray(ignoreList);
		}

		Godot.Collections.Dictionary result = spaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			Vector3 hitPos = (Vector3)result["position"];
			Vector3 normal = (Vector3)result["normal"];
			Node collider = (Node)result["collider"];

			return new()
			{
				Origin = origin,
				Direction = direction.Normalized(),
				Position = hitPos,
				Normal = normal,
				Distance = (origin - hitPos).Length(),
				Instance = ColliderToInstance(collider)
			};
		}

		return null;
	}

	[ScriptMethod]
	public RayResult[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance = 1000, Instance[]? ignoreList = null)
	{
		PhysicsDirectSpaceState3D spaceState = Root.World3D.DirectSpaceState;
		Godot.Collections.Array<Rid> ignoreRids = [];
		List<RayResult> rayResults = [];

		if (ignoreList != null)
		{
			ignoreRids = PhysicalsToArray(ignoreList);
		}

		while (true)
		{
			Godot.Collections.Dictionary result = spaceState.IntersectRay(new PhysicsRayQueryParameters3D
			{
				From = origin,
				To = origin + direction.Normalized() * maxDistance,
				CollideWithAreas = true,
				CollideWithBodies = true,
				Exclude = ignoreRids
			});

			if (result.Count == 0)
				break;

			Vector3 hitPos = (Vector3)result["position"];
			Vector3 normal = (Vector3)result["normal"];
			Rid colliderRid = (Rid)result["rid"];
			ignoreRids.Add(colliderRid);
			Node collider = (Node)result["collider"];

			rayResults.Add(new()
			{
				Origin = origin,
				Direction = direction.Normalized(),
				Position = hitPos,
				Normal = normal,
				Distance = (origin - hitPos).Length(),
				Instance = ColliderToInstance(collider)
			});
		}

		return [.. rayResults];
	}

	private static Instance? ColliderToInstance(Node collider)
	{
		Instance? instance = null;

		if (collider is Area3D a3d)
		{
			instance = Physical.GetPhysicalFromCollider(a3d);
		}

		if (collider is RigidBody3D r)
		{
			instance = (Instance?)GetNetObjFromProxy(r);
		}

		return instance;
	}

	[ScriptMethod]
	public Instance[] OverlapSphere(Vector3 origin, float radius, Instance[]? ignoreList = null)
	{
		Transform3D t = new(Basis.Identity, origin);

		PhysicsShapeQueryParameters3D query = new()
		{
			Shape = new SphereShape3D() { Radius = radius / 2 },
			Transform = t,
			CollideWithAreas = true,
			CollideWithBodies = true
		};

		return PerformOverlap(ignoreList, query);
	}

	[ScriptMethod]
	public Instance[] OverlapBox(Vector3 pos, Vector3 size, Vector3 rot, Instance[]? ignoreList = null)
	{
		Transform3D t = new()
		{
			Origin = pos
		};
		Quaternion q = Quaternion.FromEuler(rot.FlipEuler());
		Basis basis = new(q);
		t.Basis = basis;
		t = t.Scaled(Vector3.One);

		PhysicsShapeQueryParameters3D query = new()
		{
			Shape = new BoxShape3D() { Size = size },
			Transform = t,
			CollideWithAreas = true,
			CollideWithBodies = true
		};

		return PerformOverlap(ignoreList, query);
	}

	private Instance[] PerformOverlap(Instance[]? ignoreList, PhysicsShapeQueryParameters3D query)
	{
		PhysicsDirectSpaceState3D spaceState = Root.World3D.DirectSpaceState;
		if (ignoreList != null)
		{
			query.Exclude = PhysicalsToArray(ignoreList);
		}

		Godot.Collections.Array<Godot.Collections.Dictionary> results = spaceState.IntersectShape(query, MaxOverlaps);
		List<Instance> intersects = [];

		foreach (Godot.Collections.Dictionary result in results)
		{
			Node collider = (Node)result["collider"];
			Instance? i = ColliderToInstance(collider);

			if (i != null)
			{
				intersects.Add(i);
			}
		}

		return [.. intersects];
	}

	[ScriptMethod, Attributes.Obsolete("Explosion can be created using Instance.New('Explosion')")]
	public void CreateExplosion(Vector3 position, float radius = 10f, float force = 5000f, bool affectAnchored = true, PTCallback? callback = null, float damage = 10000f)
	{
		Explosion explod = New<Explosion>();
		explod.Position = position;
		explod.Radius = radius;
		explod.Force = force;
		explod.AffectAnchored = affectAnchored;
		explod.Damage = damage;
		if (callback != null)
		{
			explod.Touched.Connect((Instance hitted) =>
			{
				callback.Invoke(hitted);
			});
		}
		explod.Parent = Root.Environment;
	}

	[ScriptMethod]
	public void RebuildNavMesh()
	{
		if (_navBaking) return;
		_navBaking = true;
		_navMesh = new()
		{
			AgentRadius = 1.25f,
			AgentHeight = 6,
			AgentMaxSlope = 70,
			CellSize = 1,
			CellHeight = 1,
			AgentMaxClimb = 1.5f
		};

		_navTemps.Clear();

		// Build navigation mesh
		foreach (Instance i in GetDescendants())
		{
			if (i is Part part)
			{
				StaticBody3D staticBody = new();
				_navRegion.AddChild(staticBody);
				_navTemps.Add(staticBody);

				CollisionShape3D collisionShape = new()
				{
					Shape = part.ColliderShape
				};
				staticBody.AddChild(collisionShape);
				collisionShape.GlobalTransform = part.GetGlobalTransform();
			}
			else if (i is Mesh m)
			{
				Node3D md = (Node3D)m.GDNode.Duplicate();
				_navRegion.AddChild(md);
				_navTemps.Add(md);
			}
		}
		_navRegion.NavigationMesh = _navMesh;
		_navRegion.BakeNavigationMesh();
	}

	[ScriptMethod]
	public Vector3 GetPointOnNavMesh(Vector3 toPoint)
	{
		return NavigationServer3D.MapGetClosestPoint(Root.World3D.NavigationMap, toPoint);
	}

	private static Godot.Collections.Array<Rid> PhysicalsToArray(Instance[] instance)
	{
		Godot.Collections.Array<Rid> rids = [];
		foreach (Instance inst in instance)
		{
			if (inst is Physical p)
			{
				rids.AddRange(p.GetRids());
			}

#if CREATOR
			if (inst is Dynamic dyn && dyn.HasBound)
			{
				rids.Add(dyn.GetBoundRid());
			}
#endif

			foreach (Instance child in inst.GetDescendants())
			{
				if (child is Physical pc)
				{
					rids.AddRange(pc.GetRids());
				}

#if CREATOR
				if (child is Dynamic dync && dync.HasBound)
				{
					rids.Add(dync.GetBoundRid());
				}
#endif
			}
		}
		return rids;
	}

	public struct RayResult : IScriptObject
	{
		[ScriptProperty] public Vector3 Origin { get; set; }
		[ScriptProperty] public Vector3 Direction { get; set; }
		[ScriptProperty] public Vector3 Position { get; set; }
		[ScriptProperty] public Vector3 Normal { get; set; }
		[ScriptProperty] public float Distance { get; set; }
		[ScriptProperty] public Instance? Instance { get; set; }

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(Origin, Direction, Position, Normal);
		}
	}
}
