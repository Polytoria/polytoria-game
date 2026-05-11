// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Creator.UI;
using Polytoria.Creator.Spatial;
using Polytoria.Datamodel.Interfaces;
#endif
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Dynamic : Instance
{
	internal Node3D GDNode3D = null!;

	private Vector3 _nodeSize = Vector3.One; // NodeSize is global
	internal Vector3 NodeSize
	{
		get => _nodeSize;
		set
		{
			_nodeSize = value;
			OnNodeSizeChanged(value);
		}
	}

	private const float MinScale = 0.001f;
	private const float LerpSpeed = 20;
	public event Action? TransformChanged;
	public event Action? ReliableTransformChanged;
	protected List<Node3D> excludedBoundNodes = [];
	private bool _hasSyncedOnce = false;
	private bool _locked;
	private bool _isFirstUpdate = true;
	private bool _isDirty = false;

	private Transform3D _oldPartTransformApplied;
	private Transform3D _oldGlobalTransformApplied;
	private Transform3D _oldLocalTransformApplied;

	private int _netTransformAuthority = 1;

	protected virtual float PositionSyncThreshold => 0.5f;
	protected virtual float RotationSyncThreshold => 5f;

#if CREATOR
	private Area3D _boundArea3D = null!;
	private CollisionShape3D _boundCollider = null!;
	private BoxShape3D _boundShape = null!;
	internal bool HasBound => _boundArea3D != null;
	internal Aabb CreatorBounds;
	private readonly static Dictionary<Node, Dynamic> _creatorProxyToDyn = [];
#endif

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Position
	{
		get
		{
			return GetGlobalPosition();
		}
		set
		{
			SetGlobalPosition(value);
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Rotation
	{
		get
		{
			Basis globalBasis = GetGlobalTransform().Basis;
			Quaternion q = globalBasis.GetRotationQuaternion();

			return MathUtils.Vector3RadToDeg(q.GetEuler());
		}
		set
		{
			GDNode3D.GlobalRotationDegrees = value.SanitizeNaN();
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Size
	{
		get => GetGlobalTransform().Basis.Scale;
		set
		{
			Vector3 scale = new Vector3(
				Mathf.Max(value.X, MinScale),
				Mathf.Max(value.Y, MinScale),
				Mathf.Max(value.Z, MinScale)
			).SanitizeNaN(MinScale);

			var oldN = NodeSize;
			NodeSize = scale;
			PropagateParentSizeChanged(oldN);
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalPosition
	{
		get
		{
			return GetLocalPosition();
		}
		set
		{
			SetLocalPosition(value);
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalRotation
	{
		get
		{
			return GDNode3D.RotationDegrees;
		}
		set
		{
			GDNode3D.RotationDegrees = value.SanitizeNaN();
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalSize
	{
		get
		{
			return NodeSize / GetParentScale();
		}
		set
		{
			var oldN = NodeSize;
			ApplyLocalSize(value);
			PropagateParentSizeChanged(oldN);

			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}

			OnPropertyChanged();
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion Quaternion
	{
		get => GetGlobalTransform().Basis.GetRotationQuaternion();
		set
		{
			Quaternion q = value;
			GDNode3D.GlobalBasis = new(q);
			OnPropertyChanged();
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion LocalQuaternion
	{
		get => GetLocalTransform().Basis.GetRotationQuaternion();
		set
		{
			Quaternion q = value;
			GDNode3D.Basis = new(q);
			OnPropertyChanged();
		}
	}

	[Editable(IsHidden = true), ScriptProperty, DefaultValue(false)]
	public bool Locked
	{
		get => _locked;
		set
		{
			if (_locked == value)
			{
				return;
			}

			_locked = value;

#if CREATOR
			Explorer.RefreshLocked(this);
#endif
			OnPropertyChanged();
		}
	}

	[SyncVar(AllowAuthorWrite = false, ServerOnly = true)]
	public int NetTransformAuthority
	{
		get => _netTransformAuthority;
		set
		{
			_netTransformAuthority = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public Vector3 Forward => GetGlobalTransform().Basis.Z.Normalized();
	[ScriptProperty] public Vector3 Right => GetGlobalTransform().Basis.X.Normalized();
	[ScriptProperty] public Vector3 Up => GetGlobalTransform().Basis.Y.Normalized();

	public override Node CreateGDNode()
	{
		return new Node3D();
	}

	public override void InitGDNode()
	{
		GDNode3D = (Node3D)GDNode;
		base.InitGDNode();
	}

	public override void PreDelete()
	{
		excludedBoundNodes.Clear();
#if CREATOR
		if (_boundArea3D != null)
		{
			_creatorProxyToDyn.Remove(_boundArea3D);
			if (Node.IsInstanceValid(_boundArea3D))
			{
				_boundArea3D.QueueFree();
				_boundArea3D.Dispose();
			}
		}
#endif
		base.PreDelete();
	}

	public override void Init()
	{
#if CREATOR
		CreateCreatorBounds();
#endif
		SetPhysicsProcessWAuthor(false);
		base.Init();
	}

	/// <summary>
	/// Set physics process state, will check first if has authority, if currently hold an authority. Physics process will always be true
	/// This is to still allow position sync from network transform owner without server interfering physics process state
	/// </summary>
	/// <param name="to"></param>
	private void SetPhysicsProcessWAuthor(bool to)
	{
		if (Root != null && Root.Network != null)
			if (NetTransformAuthority == Root.Network.LocalPeerID)
			{
				SetPhysicsProcess(true);
				return;
			}
		SetPhysicsProcess(to);
	}

	public override void Ready()
	{
		if (Root != null && Root.Network != null && Root.Network.IsServer)
		{
			UpdateNetTransformReliable();
		}
		base.Ready();
	}

	private Transform3D? _lastSentTransform;
	private Transform3D _netTransform;
	private Transform3D _currentTransform;
	private bool _lerpUnreliable = false;

	/// <summary>
	/// Set if netwwork transform will be update automatically once setter called
	/// set this to false if you update them manually every frame via UpdateNetTransform()
	/// </summary>
	public bool AutoUpdateNetTransform { get; internal set; } = true;

	/// <summary>
	/// Set to true if transform will be overrided, essentially ignoring network transform 
	/// </summary>
	public bool OverrideNetworkTransform { get; internal set; } = false;

	/// <summary>
	/// Virtual function to notify when node size changed
	/// </summary>
	/// <param name="newSize"></param>
	internal virtual void OnNodeSizeChanged(Vector3 newSize) { }

	public void UpdateTransformTick(double delta)
	{
		if (!_lerpUnreliable) { return; }

		Transform3D old = _currentTransform;

		UpdateTransform(delta);

		if (_currentTransform != old)
		{
			InvokeTransformChanged();
		}
	}

	private void UpdateTransform(double delta)
	{
		if (!_isDirty) return;
		// If has transform authority, no need to update yourself
		if (NetTransformAuthority == Root.Network.LocalPeerID) return;

		float positionDistance = _currentTransform.Origin.DistanceTo(_netTransform.Origin);

		// Check if this is the first update or if distance is too large
		if (_isFirstUpdate || positionDistance > 8f)
		{
			// Snap directly to target
			_currentTransform = _netTransform;
			_isFirstUpdate = false;
			_isDirty = false;

			// Reset velocity on snapped
			if (this is Physical phy)
				phy.Velocity = Vector3.Zero;

			SetLocalTransform(_currentTransform);
		}
		else
		{
			// Lerp position and rotation
			Vector3 newPosition = _currentTransform.Origin.Lerp(_netTransform.Origin, (float)(delta * LerpSpeed));
			Quaternion currentRotation = _currentTransform.Basis.GetRotationQuaternion().Normalized();
			Quaternion targetRotation = _netTransform.Basis.GetRotationQuaternion().Normalized();
			Quaternion newRotation = currentRotation.Slerp(targetRotation, (float)(delta * LerpSpeed));

			Vector3 newScale = _netTransform.Basis.Scale;

			_currentTransform = new Transform3D(new Basis(newRotation).Scaled(newScale), newPosition);

			// Check if close enough to snap to final position
			if (positionDistance < 0.01f && currentRotation.AngleTo(targetRotation) < 0.1f)
			{
				_isDirty = false;
				_currentTransform = _netTransform;
			}

			// TODO: Could have some rework here?
			// If using SetLocalTransformRaw directly, the part size would appear bigger than usual.
			if (this is Part)
			{
				Transform3D setto = new(new Basis(newRotation), _currentTransform.Origin);

				// Only update when changed
				if (!_oldPartTransformApplied.IsEqualApprox(setto))
				{
					_oldPartTransformApplied = setto;

					// Update raw without scale, scale is already handled via reliable update
					GDNode3D.Transform = setto;
				}
			}
			else
			{
				// Update all on non parts, scale data might be carried over
				SetLocalTransformRaw(_currentTransform);
			}
		}
	}

	[ScriptMethod]
	public void LookAt(object target)
	{
		LookAt(target, Vector3.Up);
	}

	[ScriptMethod]
	public void LookAt(object target, Vector3 up)
	{
		Vector3 pos;
		if (target is Vector3 targetPos)
		{
			pos = targetPos;
		}
		else if (target is Dynamic dyn)
		{
			pos = dyn.Position;
		}
		else
		{
			throw new InvalidOperationException("LookAt Target is invalid");
		}

		GDNode3D.LookAt(pos, up);

		UpdateNetTransformReliable();
	}

	[ScriptMethod]
	public void Translate(Vector3 translation)
	{
		SetGlobalTransform(GetGlobalTransform().Translated(translation));
		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	[ScriptMethod]
	public void RotateAround(Vector3 point, Vector3 axis, float angle)
	{
		Transform3D transform = GetGlobalTransform();

		transform.Origin -= point;

		Basis rotation = new(axis.Normalized(), Mathf.DegToRad(angle));

		transform.Basis = rotation * transform.Basis;
		transform.Origin = rotation.Xform(transform.Origin);

		transform.Origin += point;

		SetGlobalTransform(transform);

		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	[ScriptMethod]
	public void Rotate(Vector3 eulerAngles)
	{
		Vector3 radians = eulerAngles * Mathf.DegToRad(1.0f);

		GDNode3D.RotateObjectLocal(Vector3.Right, radians.X);
		GDNode3D.RotateObjectLocal(Vector3.Up, radians.Y);
		GDNode3D.RotateObjectLocal(Vector3.Back, radians.Z);

		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	// NOTE: Update operations needs transform to be force updated as godot does not update them instantly
	protected void UpdateNetTransform()
	{
		if (Root == null || Root.Network == null) return;

		ForceUpdateTransform();
		Transform3D current = GetLocalTransform();

		if (_lastSentTransform is Transform3D lastSent)
		{
			if (!HasChangedForNetworkSync(lastSent, current))
			{
				return;
			}
		}

		_lastSentTransform = current;

		InvokeTransformChanged();
		if (!Root.IsLoaded) return;
		SendNetTransformUnreliable();
	}

	protected void UpdateNetTransformReliable()
	{
		if (Root == null || Root.Network == null) return;

		ForceUpdateTransform();
		Transform3D current = GetLocalTransform();

		// Only send if changed
		if (_lastSentTransform != null && _lastSentTransform.Value.IsEqualApprox(current))
			return;

		_lastSentTransform = current;

		InvokeTransformChanged();
		if (!Root.IsLoaded) return;
		SendNetTransformReliable();
	}

	protected void SendNetTransformUnreliable(bool lerp = true)
	{
		if (Root == null || Root?.Network == null) { return; }

		UpdateCurrentTransformCache();

		if (!Root.Network.IsServer)
		{
			// Send transform to server
			Root.Network.TransformSync.SendTransformToServer(this, lerp);
		}
		else
		{
			// Server broadcasts to all clients
			Root.Network.TransformSync.BroadcastTransformFromServer(this, lerp, reliable: false);
		}
	}

	protected void SendNetTransformReliable(bool lerp = false)
	{
		if (Root == null || Root?.Network == null) return;
		_lerpUnreliable = false;

		UpdateCurrentTransformCache();
		ReliableTransformChanged?.Invoke();

		if (Root.Network.IsServer)
		{
			Root.Network.TransformSync.BroadcastTransformFromServer(this, lerp, reliable: true);
		}

		// Cannot broadcast as reliable in client, values are ignored in client
	}

	/// <summary>
	/// Must be called after manual transform update
	/// </summary>
	internal void UpdateCurrentTransformCache()
	{
		if (!GDNode3D.IsInsideTree()) return;
		ForceUpdateTransform();
		Transform3D newt = GetLocalTransform();
		if (newt != _currentTransform)
		{
			if (_hasSyncedOnce)
			{
				InvokeTransformChanged();
			}
			else
			{
				_hasSyncedOnce = true;
			}
		}
		_currentTransform = newt;
	}

	/// <summary>
	/// Function for processing transform, can be used for sanity checks
	/// </summary>
	/// <param name="fromPeer"></param>
	/// <param name="newTransform"></param>
	/// <returns></returns>
	internal virtual Transform3D TransformNetworkPass(int fromPeer, Transform3D newTransform)
	{
		return newTransform;
	}

	internal virtual bool TransformNetworkCheck(Transform3D newTransform)
	{
		return true;
	}

	internal void UpdateTransformFromNet(Transform3D transform, bool isReliable, bool lerpTransform)
	{
		if (OverrideNetworkTransform) return;
		_netTransform = transform;
		_isDirty = true;

		// TODO: SetPhysicsProcess affects Physical.cs's tick, but could also affect other behaviour.
		// Maybe we need to make something dedicated to physical tick?

		// temporary set to disable lerping on non player
		// object seems to glitch weirdly when lerping
		// TODO: come back and fix this
		if (Root.Network.IsServer || !lerpTransform)
		{
			_lerpUnreliable = false;
			SetPhysicsProcessWAuthor(false);

			_currentTransform = transform;
			SetLocalTransform(transform);
		}
		else if (lerpTransform && !Root.Network.IsServer)
		{
			_lerpUnreliable = true;
			SetPhysicsProcessWAuthor(true);
		}

		if (isReliable)
		{
			ReliableTransformChanged?.Invoke();
		}

		InvokeTransformChanged();
	}

#if CREATOR
	private void CreateCreatorBounds()
	{
		if (Root == null) return;
		if (Root.SessionType != World.SessionTypeEnum.Creator) return;

		_boundArea3D = new()
		{
			Monitorable = true,
			Monitoring = false,
		};
		SetCreatorBoundActive(true);
		_creatorProxyToDyn[_boundArea3D] = this;

		_boundShape = new();

		_boundCollider = new()
		{
			Shape = _boundShape
		};

		_boundArea3D.AddChild(_boundCollider);
		Root.Environment.GDNode.AddChild(_boundArea3D, @internal: Node.InternalMode.Back);

		UpdateCreatorBounds();
	}

	internal void UpdateCreatorBounds()
	{
		if (_boundShape == null) return;
		if (Root == null) return;
		if (Root.SessionType != World.SessionTypeEnum.Creator) return;

		Aabb bound = CalculateBounds();

		CreatorBounds = bound;

		_boundShape.Size = bound.Size;
		_boundCollider.Position = bound.GetCenter();
	}

	public static Dynamic? GetDynFromCreatorBounds(Node collider)
	{
		if (_creatorProxyToDyn.TryGetValue(collider, out Dynamic? dyn)) return dyn;
		return null;
	}

	internal Rid GetBoundRid()
	{
		return _boundArea3D.GetRid();
	}

	internal void RefreshCreatorBound()
	{
		UpdateCreatorBounds();
		SetCreatorBoundActive(!IsHidden);
	}

	private void SetCreatorBoundActive(bool to)
	{
		if (_boundArea3D == null) return;
		// Ignore model/physical model and camera
		if (to && this is not IGroup and not Camera)
		{
			if (this is Physical p && p.CanCollide)
			{
				_boundArea3D.CollisionLayer = (1 << 3);
			}
			else
			{
				_boundArea3D.CollisionLayer = (1 << 2);
			}
		}
		else
		{
			_boundArea3D.CollisionLayer = 0;
		}
	}

	internal void PropagateUpdateCreatorBounds()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is Dynamic dyn)
			{
				dyn.PropagateUpdateCreatorBounds();
				dyn.UpdateCreatorBounds();
			}
		}
		UpdateCreatorBounds();
	}
#endif

	internal void InvokeTransformChanged()
	{
#if CREATOR
		if (Root.CreatorContext != null && Root.CreatorContext.Gizmos != null)
		{
			if (!Root.CreatorContext.Gizmos.HoveringGizmos && !Root.CreatorContext.Gizmos.IsDraggingDynamic)
			{
				// Update creator bounds if not changed by gizmos
				UpdateCreatorBounds();
			}
		}
#endif

		// Notify transform change without sync to clients
		OnPropertyChanged(nameof(Position), false);
		OnPropertyChanged(nameof(Rotation), false);
		OnPropertyChanged(nameof(Size), false);
		OnPropertyChanged(nameof(LocalPosition), false);
		OnPropertyChanged(nameof(LocalRotation), false);
		OnPropertyChanged(nameof(LocalSize), false);

		TransformChanged?.Invoke();
		foreach (Instance item in GetChildren())
		{
			if (item is Dynamic dyn)
			{
				dyn.InvokeTransformChanged();
			}
		}

		// Destroy entity/rigidbodies under part destroy height
		if (Root != null && Root.Environment != null && this is Entity or RigidBody)
		{
			if (Position.Y <= Root.Environment.PartDestroyHeight)
			{
				// If not client, ignore PartDestroyHeight rule
				if (Root.SessionType != World.SessionTypeEnum.Client) return;

				// If network is not ready, return
				if (!IsNetworkReady) return;

				Delete();
			}
		}
	}

	internal void PropagateParentSizeChanged(Vector3 oldParentSize)
	{
		foreach (Instance item in GetChildren())
		{
			if (item is Dynamic dyn)
			{
				var oldDynSize = dyn.NodeSize;
				Vector3 cachedLocalSize = dyn.NodeSize / oldParentSize;

				dyn.ApplyLocalSize(cachedLocalSize);

				// Apply position
				Vector3 sizeRatio = NodeSize / oldParentSize;
				dyn.GDNode3D.Position *= sizeRatio;

				dyn.NotifySizeChange();
				dyn.PropagateParentSizeChanged(oldDynSize);
			}
		}
	}

	internal void ApplyLocalSize(Vector3 localSize)
	{
		Vector3 scale = new Vector3(
			Mathf.Max(localSize.X, MinScale),
			Mathf.Max(localSize.Y, MinScale),
			Mathf.Max(localSize.Z, MinScale)
		).SanitizeNaN(MinScale);

		Vector3 parentScale = GetParentScale();
		NodeSize = scale * parentScale;
	}

	internal void NotifySizeChange()
	{
		OnPropertyChanged(nameof(LocalSize));
		OnPropertyChanged(nameof(Size));
	}

	public override void HiddenChanged(bool to)
	{
		// Player cannot be hidden
		if (this is Player) return;

		GDNode3D.Visible = !to;

#if CREATOR
		if (_boundArea3D != null)
		{
			_boundArea3D.Monitorable = !to;
			RefreshCreatorBound();
		}
#endif

		base.HiddenChanged(to);
	}

	internal Vector3 GetGlobalPosition()
	{
		return GDNode3D.GlobalPosition;
	}

	internal void SetGlobalPosition(Vector3 to)
	{
		GDNode3D.GlobalPosition = to;
		ForceUpdateTransform();
	}

	internal Vector3 GetLocalPosition()
	{
		return GetLocalTransform().Origin / GetParentScale();
	}

	internal void SetLocalPosition(Vector3 to)
	{
		var t = GetLocalTransform();
		SetLocalTransform(new Transform3D(t.Basis, to * GetParentScale()));
	}

	internal Transform3D GetGlobalTransform()
	{
		var t = GDNode3D.GlobalTransform;
		return t * Transform3D.Identity.Scaled(NodeSize);
	}

	internal Transform3D GetLocalTransform()
	{
		var t = GDNode3D.Transform;
		return t * Transform3D.Identity.Scaled(NodeSize / GetParentScale());
	}

	internal void SetGlobalTransformRaw(Transform3D to)
	{
		if (!GDNode3D.IsInsideTree()) return;
		if (_oldGlobalTransformApplied == to) return;
		_oldGlobalTransformApplied = to;

		Vector3 scale = new Vector3(
			to.Basis.Column0.Length(),
			to.Basis.Column1.Length(),
			to.Basis.Column2.Length()
		).SanitizeNaN();

		var oldN = NodeSize;
		NodeSize = scale;

		GDNode3D.GlobalTransform = new(to.Basis.Orthonormalized(), to.Origin.SanitizeNaN());
		PropagateParentSizeChanged(oldN);
	}

	internal void SetLocalTransformRaw(Transform3D to)
	{
		if (!GDNode3D.IsInsideTree()) return;
		if (_oldLocalTransformApplied == to) return;
		_oldLocalTransformApplied = to;

		Vector3 scale = new Vector3(
			to.Basis.Column0.Length(),
			to.Basis.Column1.Length(),
			to.Basis.Column2.Length()
		).SanitizeNaN();

		var oldN = NodeSize;
		NodeSize = scale * GetParentScale();
		GDNode3D.Transform = new(to.Basis.Orthonormalized(), to.Origin.SanitizeNaN());
		PropagateParentSizeChanged(oldN);
	}

	private Vector3 GetParentScale()
	{
		if (Parent is Dynamic p)
			return p.NodeSize;
		return Vector3.One;
	}

	internal void SetGlobalTransform(Transform3D to)
	{
		SetGlobalTransformRaw(to);
		UpdateCurrentTransformCache();
	}

	internal void SetLocalTransform(Transform3D to)
	{
		SetLocalTransformRaw(to);
		UpdateCurrentTransformCache();
	}

	internal void ForceUpdateTransform()
	{
		if (!GDNode3D.IsInsideTree()) return;
		GDNode3D.ForceUpdateTransform();
	}

	internal void CopyTransformTo(Dynamic target, bool asGlobal = false)
	{
		target.NodeSize = NodeSize;

		if (asGlobal)
		{
			target.GDNode3D.GlobalTransform = GDNode3D.GlobalTransform;
		}
		else
		{
			target.GDNode3D.Transform = GDNode3D.Transform;
		}

		target.UpdateCurrentTransformCache();
	}

	protected bool HasChangedForNetworkSync(Transform3D a, Transform3D b)
	{
		float dst = a.Origin.DistanceTo(b.Origin);
		if (dst > PositionSyncThreshold)
		{
			return true;
		}

		Quaternion aRot = a.Basis.GetRotationQuaternion().Normalized();
		Quaternion bRot = b.Basis.GetRotationQuaternion().Normalized();

		float deg = Mathf.RadToDeg(aRot.AngleTo(bRot));
		if (deg > RotationSyncThreshold)
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// GetBounds for scripting uses, for internal use. use CalculateBounds
	/// </summary>
	/// <returns></returns>
	[ScriptMethod]
	public Aabb GetBounds()
	{
		return CalculateBounds();
	}

	internal void SetVisualMaskLayer(int layer, bool to)
	{
		foreach (Node item in GetDescendantsInternal(GDNode))
		{
			if (item is VisualInstance3D v)
			{
				v.SetLayerMaskValue(layer, to);
			}
		}
	}

	internal Aabb CalculateBounds()
	{
		Aabb? bounds = null;

		Instance[] all = [this, .. GetDescendants()];

		foreach (Instance item in all)
		{
			if (item is Part part)
			{
				Transform3D t = part.GetGlobalTransform();

				Vector3 localSize = part.Size;
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

				Aabb pBounds = new(center - worldExtents, worldExtents * 2);


				if (bounds == null)
				{
					bounds = pBounds;
				}
				else
				{
					bounds = bounds.Value.Merge(pBounds);
				}
			}
			else if (item is Light l)
			{
				Transform3D t = l.GetGlobalTransform();

				Aabb pBounds = new(t.Origin - Vector3.One, Vector3.One * 2);

				if (bounds == null)
				{
					bounds = pBounds;
				}
				else
				{
					bounds = bounds.Value.Merge(pBounds);
				}
			}
			else if (item is Dynamic dyn)
			{
				Node[] scanNodes = [GDNode3D, .. GetNonInstanceDescendants(dyn.GDNode3D)];

				foreach (Node n in scanNodes)
				{
#if CREATOR
					if (n is ISpatial) continue;
#endif
					if (n is VisualInstance3D v3d)
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
				}
			}
		}

		return bounds ?? new(GetGlobalPosition(), Vector3.One * 0.5f);
	}

	public virtual Aabb GetSelfBound() { return default; }
}
