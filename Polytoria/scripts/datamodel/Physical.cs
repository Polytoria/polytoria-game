// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Polytoria.Datamodel;

[Abstract]
public partial class Physical : Dynamic
{
	public const float MinMass = 0.01f;
	private static readonly Dictionary<CollisionObject3D, Physical> _bodyToPhysical = [];
	private static readonly Dictionary<Node, Physical> _proxyToPhysical = [];
	private static readonly ConditionalWeakTable<CollisionShape3D, RemoteLinkConfig> _remoteLinkConfigs = [];
	private static readonly ConditionalWeakTable<CollisionShape3D, TrackedNodesState> _trackedNodes = [];

	private sealed class RemoteLinkConfig
	{
		public Node? Target;
		public Vector3 Offset = Vector3.Zero;
		public bool HasOffset;
	}

	private sealed class TrackedNodesState
	{
		public List<Node> TouchAreaNodes { get; } = [];
		public List<Node> CollisionSyncNodes { get; } = [];
	}

	public virtual float SyncInterval { get; protected set; } = 0.1f;
	private const float TouchedGapCheck = 20f;
	private bool _anchored = true;
	private bool _canCollide = true;
	private Vector3 _velocity = Vector3.Zero;
	private Vector3 _angularVelocity = Vector3.Zero;

	private bool _netEnsureTouchArea = false;

	private CollisionObject3D? _registeredCollisionBody;

	private int _touchedListenerCount = 0;
	private bool _canTouch = false;

	private double _syncClock = 0;

	private readonly Dictionary<Physical, int> _touchContacts = [];

	public List<CollisionShape3D> CollisionShapes = [];
	public List<CollisionShape3D> AreaCollisionShapes = [];
	public List<CollisionShape3D> CollisionRootShapes = [];
	private readonly HashSet<CollisionShape3D> _pendingAreaShapes = [];

	[ScriptProperty] public PTSignal<Physical> Touched { get; private set; } = new();
	[ScriptProperty] public PTSignal<Physical> TouchEnded { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseEnter { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseExit { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> Clicked { get; private set; } = new();

	public event Action<CollisionShape3D>? CollisionShapeAdded;
	public event Action<CollisionShape3D>? CollisionShapeRemoved;

	internal Area3D? PhysicalArea { get; private set; }

	[SyncVar]
	public bool NetEnsureTouchArea
	{
		get => _netEnsureTouchArea;
		set
		{
			_netEnsureTouchArea = value;

			if (_netEnsureTouchArea)
			{
				EnsureTouchArea();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public virtual bool Anchored
	{
		get => _anchored;
		set
		{
			if (_anchored == value)
			{
				return;
			}

			bool oldVal = _anchored;
			_anchored = value;

			if (oldVal != _anchored)
			{
				UpdateFreeze();
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public virtual bool CanCollide
	{
		get => _canCollide;
		set
		{
			if (_canCollide == value)
			{
				return;
			}

			_canCollide = value;

			UpdateCollision();

			OnPropertyChanged();
		}
	}

	internal void UpdateFreeze()
	{
		bool finalVal = _anchored;

		if (Root != null && Root.Network != null)
		{
			if (Root.SessionType == World.SessionTypeEnum.Creator || !Root.IsLoaded)
			{
				finalVal = true;
			}

			// Freeze the object on non physics authority
			if (Root.SessionType == World.SessionTypeEnum.Client && Root.Network.LocalPeerID != NetTransformAuthority && ExistInNetwork)
			{
				finalVal = true;
			}

			if (IsHidden)
			{
				finalVal = true;
			}
		}

		ApplyFreeze(finalVal);

		if (Root != null && Root.Network != null)
		{
			// Ignore player
			if (Root.Network.IsServer && this is not Player)
			{
				AutoUpdateNetTransform = finalVal;
			}
		}

		if (!OverridePhysicsProcess)
		{
			SetPhysicsProcess(!_anchored);
		}
	}

	protected virtual void ApplyFreeze(bool to) { }

	internal void UpdateCollision()
	{
		if (IsDeleted) return;
		if (OverrideCanCollide)
		{
			SetCollisionDisabled(!OverrideCanCollideTo);
			return;
		}

		// Set each collision
		if (!IsHidden)
		{
			// Stop collision override if player's not ready
			if (this is Player plr && !plr.IsReady) { return; }
			bool setTo = !_canCollide;
			SetCollisionDisabled(setTo);

			if (setTo)
			{
				// Ensure touch area on non collide-able physicals, so other can still detect this object
				EnsureTouchArea();
			}
		}
		else
		{
			SetCollisionDisabled(true);
		}

#if CREATOR
		RefreshCreatorBound();
#endif
	}

	internal void SetCollisionDisabled(bool disabled)
	{
		foreach (CollisionShape3D c in CollisionShapes.ToArray())
		{
			if (!Node.IsInstanceValid(c)) continue;
			c.Disabled = disabled;
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public virtual Vector3 Velocity
	{
		get
		{
			if (this is NPC npc)
			{
				return npc.CharacterVelocity;
			}

			return _velocity;
		}
		set
		{
			_velocity = value;

			var setto = _velocity;

			if (this is Player plr)
			{
				plr.LastVelocity = _velocity;
			}

			if (this is NPC npc)
			{
				npc.CharacterVelocity = setto;
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public virtual Vector3 AngularVelocity
	{
		get
		{
			return _angularVelocity;
		}
		set
		{
			_angularVelocity = value;
			OnPropertyChanged();
		}
	}

	public Physical? PhysicalRoot { get; private set; }

	internal bool OverrideCanCollide = false;
	internal bool OverrideCanCollideTo = false;
	internal bool OverridePhysicsProcess = false;

	public override void HiddenChanged(bool to)
	{
		UpdateCollision();

		if (!to && PhysicalArea != null)
		{
			// create pending shapes
			foreach (var shape in _pendingAreaShapes.ToArray())
			{
				if (Node.IsInstanceValid(shape))
					CreateAreaShape(shape);
			}

			_pendingAreaShapes.Clear();
		}

		foreach (CollisionShape3D c in AreaCollisionShapes)
		{
			c.Disabled = to;
		}

		base.HiddenChanged(to);
	}

	public override void EnterTree()
	{
		PhysicalRoot = null;
		Instance? current = Parent;

		// Find physical until parent is not physical (top physical as root)
		while (current != null)
		{
			Type ct = current.GetType();

			if (current is Physical pr)
			{
				PhysicalRoot = pr;
			}
			else
			{
				break;
			}

			if (ct.IsDefined(typeof(PhysicalRootStopAttribute), false))
			{
				break;
			}

			current = current.Parent;
		}

		base.EnterTree();

		foreach (CollisionShape3D item in CollisionShapes.ToArray())
		{
			PostCollisionShapeUpdate(item);
		}
	}

	public override void Init()
	{
		Touched.Subscribed += OnTouchSubscribed;
		Touched.Unsubscribed += OnTouchUnsubscribed;

		Clicked.Subscribed += OnClickSubscribed;
		Clicked.Subscribed += OnTouchSubscribed;
		Clicked.Unsubscribed += OnTouchUnsubscribed;

		TouchEnded.Subscribed += OnTouchSubscribed;
		TouchEnded.Unsubscribed += OnTouchUnsubscribed;

		base.Init();

		ApplyFreeze(true);

		_proxyToPhysical[GDNode] = this;

		if (this is Entity e)
		{
			e.GDRigidBody.GravityScale = 2;
		}

		if (Root != null)
		{
			if (Root.IsLoaded)
			{
				OnRootReady();
			}
			else
			{
				Root.Loaded.Once(OnRootReady);
			}
		}
	}

	public override void PreDelete()
	{
		ClearCollisionBody();
		Root?.Loaded.Disconnect(OnRootReady);
		// _proxyToPhysical.Remove(PhysicalArea);
		_proxyToPhysical.Remove(GDNode);

		if (PhysicalArea != null)
		{
			_proxyToPhysical.Remove(PhysicalArea);

			PhysicalArea.AreaEntered -= AreaEntered;
			PhysicalArea.AreaExited -= AreaExited;

			PhysicalArea.BodyShapeEntered -= BodyShapeEntered;
			PhysicalArea.BodyShapeExited -= BodyShapeExited;

			if (Node.IsInstanceValid(PhysicalArea))
			{
				PhysicalArea.QueueFree();
			}

			PhysicalArea = null;
		}

		AreaCollisionShapes.Clear();
		CollisionRootShapes.Clear();
		CollisionShapes.Clear();
		_touchContacts.Clear();

		Touched.Subscribed -= OnTouchSubscribed;
		Touched.Unsubscribed -= OnTouchUnsubscribed;

		Clicked.Subscribed -= OnClickSubscribed;
		Clicked.Subscribed -= OnTouchSubscribed;
		Clicked.Unsubscribed -= OnTouchUnsubscribed;

		TouchEnded.Subscribed -= OnTouchSubscribed;
		TouchEnded.Unsubscribed -= OnTouchUnsubscribed;

		base.PreDelete();
	}

	public override void Ready()
	{
		RefreshCollisionBody();

		foreach (CollisionShape3D shape in CollisionShapes.ToArray())
		{
			AttachCollisionShape(shape);
			EnsureRemoteTransform(shape);
		}

		UpdateCollision();
		UpdateFreeze();
		base.Ready();
	}

	private CollisionObject3D? GetCollisionObject()
	{
		return GDNode as CollisionObject3D;
	}

	private void RefreshCollisionBody()
	{
		CollisionObject3D? body = GetCollisionObject();
		if (_registeredCollisionBody == body)
			return;

		if (_registeredCollisionBody != null)
		{
			_bodyToPhysical.Remove(_registeredCollisionBody);
		}

		_registeredCollisionBody = body;

		if (body != null)
		{
			_bodyToPhysical[body] = this;
		}
	}

	private void ClearCollisionBody()
	{
		if (_registeredCollisionBody != null)
		{
			_bodyToPhysical.Remove(_registeredCollisionBody);
			_registeredCollisionBody = null;
		}
	}

	public static Physical? GetPhysicalFromBody(CollisionObject3D body)
	{
		if (_bodyToPhysical.TryGetValue(body, out Physical? val)) return val;
		return null;
	}

	private void OnTouchSubscribed()
	{
		_touchedListenerCount++;
		EnableCanTouch();
	}

	private void OnTouchUnsubscribed()
	{
		_touchedListenerCount--;
		if (_touchedListenerCount <= 0)
		{
			DisableCanTouch();
		}
	}

	private void OnClickSubscribed()
	{
		NetEnsureTouchArea = true;
	}

	private void OnRootReady()
	{
		UpdateFreeze();
	}

	internal void EnableCanTouch()
	{
		EnsureTouchArea();

		if (!_canTouch)
		{
			_canTouch = true;
			PT.CallOnMainThread(() =>
			{
				if (PhysicalArea != null)
				{
					PhysicalArea.Monitoring = true;
				}
			});
		}
	}

	internal void DisableCanTouch()
	{
		if (_canTouch)
		{
			_canTouch = false;
			PT.CallOnMainThread(() =>
			{
				if (PhysicalArea != null)
				{
					PhysicalArea.Monitoring = false;
				}
			});
		}
	}

	protected void UpdateVelocityInternal(Vector3 vel)
	{
		_velocity = vel;
	}

	public override void PhysicsProcess(double delta)
	{
		UpdateTransformTick(delta);
		if (Root == null || Root?.Network == null) { return; }

		_syncClock += delta;

		// Sync if has authority and not anchored, if so. sync in interval
		if (_syncClock > SyncInterval)
		{
			_syncClock = 0;
			if (NetTransformAuthority == Root.Network.LocalPeerID && !Anchored)
			{
				UpdateNetTransform();
			}
		}
		base.PhysicsProcess(delta);
	}

	[ScriptMethod]
	public void SetNetworkAuthority(Player? plr)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Set authority can only be called from server");
		int peerId = plr?.PeerID ?? 1;
		NetTransformAuthority = peerId;
		RpcId(peerId, nameof(NetGivenAuthority));
		UpdateFreeze();

		// if is RigidBody, give authority to child too
		if (this is RigidBody)
		{
			foreach (var item in GetChildren())
			{
				if (item is Physical phy)
				{
					phy.SetNetworkAuthority(plr);
				}
			}
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetGivenAuthority()
	{
		NetTransformAuthority = Root.Network.LocalPeerID;
		UpdateFreeze();
	}

	/// <summary>
	/// Add collision shape, this is used for mirroring collision shapes to other body types
	/// </summary>
	/// <param name="collisionShape"></param>
	internal void AddCollisionShape(CollisionShape3D collisionShape)
	{
		if (CollisionShapes.Contains(collisionShape)) return;
		CollisionShapes.Add(collisionShape);
		AttachCollisionShape(collisionShape);
		EnsureRemoteTransform(collisionShape);
		CollisionShapeAdded?.Invoke(collisionShape);

		CreateAreaShape(collisionShape);
	}

	/// <summary>
	/// This function must be called if collision shape has been updated
	/// </summary>
	/// <param name="collisionShape">Target collision shape</param>
	internal void PostCollisionShapeUpdate(CollisionShape3D collisionShape)
	{
		RemoveCollisionShape(collisionShape, false);
		AddCollisionShape(collisionShape);
	}

	/// <summary>
	/// Remove collision shape from the mirror
	/// </summary>
	/// <param name="collisionShape">Target collision shape</param>
	/// <param name="free">Free the shape now?</param>
	internal void RemoveCollisionShape(CollisionShape3D collisionShape, bool free = true)
	{
		if (!CollisionShapes.Contains(collisionShape)) return;
		CollisionShapes.Remove(collisionShape);
		_pendingAreaShapes.Remove(collisionShape);

		if (free)
			_remoteLinkConfigs.Remove(collisionShape);

		CleanupTrackedNodes(collisionShape, static state => state.TouchAreaNodes, true);
		CleanupTrackedNodes(collisionShape, static state => state.CollisionSyncNodes, false);

		RevertCollisionShape(collisionShape);

		CollisionShapeRemoved?.Invoke(collisionShape);

		if (free)
		{
			collisionShape.QueueFree();
		}
	}

	private void CleanupTrackedNodes(CollisionShape3D collisionShape, Func<TrackedNodesState, List<Node>> selector, bool removeAreaShapes)
	{
		if (!_trackedNodes.TryGetValue(collisionShape, out TrackedNodesState? state)) return;

		List<Node> createdNodes = selector(state);
		foreach (Node node in createdNodes)
		{
			if (node != null && Node.IsInstanceValid(node))
			{
				if (removeAreaShapes && node is CollisionShape3D shape)
				{
					AreaCollisionShapes.Remove(shape);
				}

				node.QueueFree();
			}
		}

		createdNodes.Clear();

		if (state.TouchAreaNodes.Count == 0 && state.CollisionSyncNodes.Count == 0)
		{
			_trackedNodes.Remove(collisionShape);
		}
	}

	internal void ClearCollisionShapes()
	{
		foreach (CollisionShape3D collision in CollisionShapes.ToArray())
		{
			RemoveCollisionShape(collision);
		}
	}


	private void CreateAreaShape(CollisionShape3D origin)
	{
		if (!Node.IsInstanceValid(origin)) return;

		// If is hidden, don't create area shape yet
		if (IsHidden && PhysicalRoot == null)
		{
			_pendingAreaShapes.Add(origin);
			return;
		}

		CreateAreaShapeInternal(origin);
	}

	internal static void SetRemoteLinkTarget(CollisionShape3D collisionShape, Node? target)
	{
		RemoteLinkConfig config = _remoteLinkConfigs.GetOrCreateValue(collisionShape);
		config.Target = target != null && Node.IsInstanceValid(target) ? target : null;
	}

	internal static void SetRemoteLinkOffset(CollisionShape3D collisionShape, Vector3 offset)
	{
		RemoteLinkConfig config = _remoteLinkConfigs.GetOrCreateValue(collisionShape);
		config.Offset = offset;
		config.HasOffset = true;
	}

	private static RemoteLinkConfig? GetRemoteLinkConfig(CollisionShape3D collisionShape)
	{
		if (_remoteLinkConfigs.TryGetValue(collisionShape, out RemoteLinkConfig? config))
		{
			return config;
		}

		return null;
	}

	private static TrackedNodesState GetTrackedNodesState(CollisionShape3D collisionShape)
	{
		return _trackedNodes.GetOrCreateValue(collisionShape);
	}

	private static bool HasValidTrackedNodes(CollisionShape3D collisionShape, Func<TrackedNodesState, List<Node>> selector)
	{
		if (!_trackedNodes.TryGetValue(collisionShape, out TrackedNodesState? state))
		{
			return false;
		}

		List<Node> trackedNodes = selector(state);
		for (int i = trackedNodes.Count - 1; i >= 0; i--)
		{
			Node node = trackedNodes[i];
			if (node != null && Node.IsInstanceValid(node))
			{
				return true;
			}

			trackedNodes.RemoveAt(i);
		}

		if (state.TouchAreaNodes.Count == 0 && state.CollisionSyncNodes.Count == 0)
		{
			_trackedNodes.Remove(collisionShape);
		}

		return false;
	}

	private static void SetTrackedNodes(CollisionShape3D collisionShape, Func<TrackedNodesState, List<Node>> selector, IEnumerable<Node> nodes)
	{
		TrackedNodesState state = GetTrackedNodesState(collisionShape);
		List<Node> trackedNodes = selector(state);
		trackedNodes.Clear();
		trackedNodes.AddRange(nodes);
	}

	private void AttachRemoteLinkNode(CollisionShape3D origin, Node3D scaleNode)
	{
		RemoteLinkConfig? config = GetRemoteLinkConfig(origin);
		Node? target = config?.Target;

		if (target != null && Node.IsInstanceValid(target))
		{
			target.AddChild(scaleNode, @internal: Node.InternalMode.Back);
		}
		else
		{
			GDNode.AddChild(scaleNode, @internal: Node.InternalMode.Back);
		}

		scaleNode.Position = config is { HasOffset: true } ? config.Offset : Vector3.Zero;
	}

	private Node3D CreateRemoteLinkNode(CollisionShape3D origin, Node target)
	{
		Node3D scaleNode = new();
		RemoteTransform3D rt = new()
		{
			UseGlobalCoordinates = true
		};
		scaleNode.AddChild(rt);

		AttachRemoteLinkNode(origin, scaleNode);

		rt.RemotePath = rt.GetPathTo(target);
		return scaleNode;
	}

	private void EnsureRemoteTransform(CollisionShape3D origin)
	{
		if (!Node.IsInstanceValid(origin)) return;

		if (HasValidTrackedNodes(origin, static state => state.CollisionSyncNodes))
		{
			return;
		}

		Node3D scaleNode = CreateRemoteLinkNode(origin, origin);
		SetTrackedNodes(origin, static state => state.CollisionSyncNodes, [scaleNode]);
	}

	private void CreateAreaShapeInternal(CollisionShape3D origin)
	{
		if (!Node.IsInstanceValid(origin) || origin.Shape == null) return;

		Shape3D sharedShape = origin.Shape;
		List<Node> createdNodes = [];

		CollisionShape3D CreateLinkedShape(Node parent)
		{
			// Create Node3D for scaling
			Node3D scaleNode = new()
			{
				Scale = new(1.01f, 1.01f, 1.01f)
			};

			CollisionShape3D newShape = new()
			{
				Shape = sharedShape,
				Disabled = IsHidden,
			};

			parent.AddChild(newShape);
			createdNodes.Add(newShape);

			RemoteTransform3D rt = new()
			{
				UseGlobalCoordinates = true
			};
			scaleNode.AddChild(rt);
			createdNodes.Add(scaleNode);

			AttachRemoteLinkNode(origin, scaleNode);

			rt.RemotePath = rt.GetPathTo(newShape);
			return newShape;
		}

		// Handle Physical Area
		if (PhysicalArea != null)
		{
			var areaShape = CreateLinkedShape(PhysicalArea);
			AreaCollisionShapes.Add(areaShape);
		}

		// Handle Physical Root
		if (PhysicalRoot != null)
		{
			PhysicalRoot.EnsureTouchArea();

			if (PhysicalRoot.PhysicalArea != null)
			{
				var areaShape2 = CreateLinkedShape(PhysicalRoot.PhysicalArea);
				AreaCollisionShapes.Add(areaShape2);
			}
		}

		SetTrackedNodes(origin, static state => state.TouchAreaNodes, createdNodes);
	}

	public static Physical? GetPhysicalFromCollider(Node collider)
	{
		if (_proxyToPhysical.TryGetValue(collider, out Physical? val)) return val;
		return null;
	}

	public static Physical? GetPhysicalFromBodyShape(CollisionObject3D body, int shapeIndex)
	{
		if (_bodyToPhysical.TryGetValue(body, out Physical? val))
			return val;

		return null;
	}

	private Node GetCollisionShapeRoot()
	{
		if (PhysicalRoot != null)
		{
			return PhysicalRoot.GDNode;
		}

		return GDNode;
	}

	private void AttachCollisionShape(CollisionShape3D origin)
	{
		if (!Node.IsInstanceValid(origin)) return;

		Node targetRoot = GetCollisionShapeRoot();
		if (!Node.IsInstanceValid(targetRoot)) return;

		if (origin.GetParent() == targetRoot)
		{
			if (!CollisionRootShapes.Contains(origin))
			{
				CollisionRootShapes.Add(origin);
			}
			return;
		}

		Transform3D global = origin.GlobalTransform;

		origin.Reparent(targetRoot);
		origin.GlobalTransform = global;

		CollisionRootShapes.Remove(origin);
		CollisionRootShapes.Add(origin);
	}

	private void RevertCollisionShape(CollisionShape3D origin)
	{
		if (!Node.IsInstanceValid(origin)) return;

		if (origin.GetParent() == GDNode)
		{
			CollisionRootShapes.Remove(origin);
			return;
		}

		Transform3D global = origin.GlobalTransform;

		origin.Reparent(GDNode);
		origin.GlobalTransform = global;

		CollisionRootShapes.Remove(origin);
	}

	internal void ApplyForceFromPlayer(Vector3 force)
	{
		if (Anchored) return;
		if (this is not Entity) return;
		((RigidBody3D)GDNode).ApplyCentralImpulse(force);
	}

	private void AreaEntered(Area3D area)
	{
		Physical? p = GetPhysicalFromCollider(area);
		if (p != null)
		{
			InternalInvokeTouched(p);
		}
	}

	private void AreaExited(Area3D area)
	{
		Physical? p = GetPhysicalFromCollider(area);
		if (p != null)
		{
			InternalInvokeTouchEnded(p);
		}
	}

	private void BodyShapeEntered(Rid bodyRid, Node3D body, long bodyShapeIndex, long localShapeIndex)
	{
		if (body is not CollisionObject3D collisionBody)
			return;

		Physical? p = GetPhysicalFromBodyShape(collisionBody, (int)bodyShapeIndex);
		if (p != null)
		{
			InternalInvokeTouched(p);
		}
	}

	private void BodyShapeExited(Rid bodyRid, Node3D body, long bodyShapeIndex, long localShapeIndex)
	{
		if (body is not CollisionObject3D collisionBody)
			return;

		Physical? p = GetPhysicalFromBodyShape(collisionBody, (int)bodyShapeIndex);
		if (p != null)
		{
			InternalInvokeTouchEnded(p);
		}
	}

	internal Rid GetRid()
	{
		if (PhysicalArea != null)
		{
			return PhysicalArea.GetRid();
		}

		return GetCollisionObject()?.GetRid() ?? default;
	}

	internal Rid[] GetRids()
	{
		List<Rid> rids = [];
		if (PhysicalArea != null)
		{
			rids.Add(PhysicalArea.GetRid());
		}

		if (GetCollisionObject() is CollisionObject3D val)
		{
			rids.Add(val.GetRid());
		}

		return [.. rids];
	}

	internal void InvokeTouched(Physical hit)
	{
		//if (!IsInstanceValid(this) || !IsInsideTree()) return;
		InternalInvokeTouched(hit);
		Rpc(nameof(NetInvokeTouched), hit.NetworkedObjectID);
	}

	internal void InvokeTouchEnded(Physical hit)
	{
		InternalInvokeTouchEnded(hit);
		Rpc(nameof(NetInvokeTouchEnded), hit.NetworkedObjectID);
	}

	internal void InvokeClicked(Player by)
	{
		InternalInvokeClicked(by);
		RpcId(1, nameof(NetInvokeClicked), by.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, AllowToServerOnly = false)]
	private void NetInvokeTouched(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr && !plr.IsDead)
		{
			// Ignore invalid touches (touches that are out of range)
			if (!IsTouchedValid(plr)) return;

			InternalInvokeTouched(plr);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, AllowToServerOnly = false)]
	private void NetInvokeTouchEnded(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr)
		{
			InternalInvokeTouchEnded(plr);
		}
	}

	private bool IsTouchedValid(Player plr)
	{
		// Check if player position is in vaild range
		return GetSelfBound().Grow(TouchedGapCheck).HasPoint(plr.GetGlobalPosition());
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetInvokeClicked(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr)
		{
			InternalInvokeClicked(plr);
		}
	}

	private void InternalInvokeTouched(Physical physical)
	{
		if (physical == this) return;
		// Ignore dead NPCs, their position could be inaccurate
		if (physical is NPC npc && npc.IsDead) return;

		// Ignore player that's not ready
		if (physical is Player plr && !plr.IsReady) return;

		if (_touchContacts.TryGetValue(physical, out int count))
		{
			_touchContacts[physical] = count + 1;
			return;
		}

		_touchContacts[physical] = 1;
		Touched.Invoke(physical);
	}

	private void InternalInvokeTouchEnded(Physical physical)
	{
		// Ignore player that's not ready
		if (physical is Player plr && !plr.IsReady) return;

		if (!_touchContacts.TryGetValue(physical, out int count))
			return;

		if (count > 1)
		{
			_touchContacts[physical] = count - 1;
			return;
		}

		_touchContacts.Remove(physical);
		TouchEnded.Invoke(physical);
	}

	private void InternalInvokeClicked(Player by)
	{
		Clicked.Invoke(by);
	}

	protected void EnsureTouchArea()
	{
		if (PhysicalArea != null) return;
		if (!Node.IsInstanceValid(GDNode3D)) return;

		PhysicalArea = new()
		{
			Monitorable = true,
			Monitoring = _canTouch,
			Scale = new(1.01f, 1.01f, 1.01f)
		};

		PhysicalArea.SetCollisionMaskValue(2, true);

		PhysicalArea.AreaEntered += AreaEntered;
		PhysicalArea.AreaExited += AreaExited;

		PhysicalArea.BodyShapeEntered += BodyShapeEntered;
		PhysicalArea.BodyShapeExited += BodyShapeExited;

		_proxyToPhysical[PhysicalArea] = this;
		GDNode3D.AddChild(PhysicalArea, false, Node.InternalMode.Front);

		foreach (CollisionShape3D item in CollisionShapes)
		{
			CreateAreaShape(item);
		}
	}

	[ScriptMethod]
	public Physical[] GetTouching()
	{
		EnableCanTouch();

		if (_touchContacts.Count == 0)
			return [];

		List<Physical> phys = [];

		foreach (var (physical, _) in _touchContacts)
		{
			if (physical == null || physical.IsDeleted)
				continue;

			phys.Add(physical);
		}

		return [.. phys];
	}

	[ScriptMethod]
	public void MovePosition(Vector3 position)
	{
		Position += position;
	}

	[ScriptMethod]
	public void MoveRotation(Vector3 rotation)
	{
		Rotation += rotation;
	}

	[ScriptMethod]
	public void AddForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		ApplyAddForce(force, mode);
	}

	internal virtual void ApplyAddForce(Vector3 force, ForceModeEnum mode) { throw new NotImplementedException(ClassName + " does not support this force function"); }

	[ScriptMethod]
	public void AddTorque(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		ApplyAddTorque(force, mode);
	}

	internal virtual void ApplyAddTorque(Vector3 force, ForceModeEnum mode) { throw new NotImplementedException(ClassName + " does not support this force function"); }

	[ScriptMethod]
	public void AddForceAtPosition(Vector3 force, Vector3 position, ForceModeEnum mode = ForceModeEnum.Force)
	{
		ApplyAddForceAtPosition(force, position, mode);
	}

	internal virtual void ApplyAddForceAtPosition(Vector3 force, Vector3 position, ForceModeEnum mode) { throw new NotImplementedException(ClassName + " does not support this force function"); }

	[ScriptMethod]
	public void AddRelativeForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		ApplyAddRelativeForce(force, mode);
	}

	internal virtual void ApplyAddRelativeForce(Vector3 force, ForceModeEnum mode) { throw new NotImplementedException(ClassName + " does not support this force function"); }

	[ScriptMethod]
	public void AddRelativeTorque(Vector3 torque, ForceModeEnum mode = ForceModeEnum.Force)
	{
		ApplyAddRelativeTorque(torque, mode);
	}

	internal virtual void ApplyAddRelativeTorque(Vector3 torque, ForceModeEnum mode) { throw new NotImplementedException(ClassName + " does not support this force function"); }

	public enum ForceModeEnum
	{
		Force,
		Acceleration,
		Impulse,
		VelocityChange
	}
}
