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
using Polytoria.Client.Networking;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.DTOs;
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
			if (_nodeSize == value) return;
			_nodeSize = value;
			OnNodeSizeChanged(value);
		}
	}

	private const float MinScale = 0.001f;
	private const float NetVelocitySlack = 50f;
	private const double NetRestDelay = NetworkTransformSync.TickInterval * 2;
	private const float NetStillSpeed = 0.1f;
	private const float NetStillStep = 0.1f;
	private const double NetWriteAckTimeout = 5;
	public event Action? TransformChanged;
	public event Action? ReliableTransformChanged;
	protected List<Node3D> excludedBoundNodes = [];
	private bool _hasSyncedOnce = false;
	private bool _locked;
	private bool _isFirstUpdate = true;
	private bool _isDirty = false;

	private static readonly HashSet<Dynamic> _pendingTransforms = [];
	private bool _pendingSyncToNet;
	private bool _netTeleport;
	private double _lastScriptMove = double.MinValue;

	private Transform3D _oldPartTransformApplied;
	private Transform3D _oldGlobalTransformApplied;
	private Transform3D _oldLocalTransformApplied;

	private int _netTransformAuthority = 1;

	protected virtual float PositionSyncThreshold => 0.05f;
	protected virtual float RotationSyncThreshold => 1f;

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
			return GetSimulatedTransform().Origin;
		}
		set
		{
			SyncPhysicsTransform();
			SetGlobalPosition(value);
			QueueTransformUpdate(AutoUpdateNetTransform, true);
		}
	}

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Rotation
	{
		get
		{
			Quaternion q = GetSimulatedTransform().Basis.GetRotationQuaternion();

			return MathUtils.Vector3RadToDeg(q.GetEuler());
		}
		set
		{
			Vector3 sanitizedValue = value.SanitizeNaN();
			SyncPhysicsTransform();
			GDNode3D.GlobalRotationDegrees = sanitizedValue;
			_oldGlobalTransformApplied = GetGlobalTransform();
			QueueTransformUpdate(AutoUpdateNetTransform, true);
		}
	}

	[Editable, ScriptProperty, CloneIgnore, SaveIgnore]
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
			SyncPhysicsTransform();
			SetLocalPosition(value);
			QueueTransformUpdate(AutoUpdateNetTransform, true);
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
			SyncPhysicsTransform();
			GDNode3D.RotationDegrees = value.SanitizeNaN();
			QueueTransformUpdate(AutoUpdateNetTransform, true);
		}
	}

	[Editable, ScriptProperty, CloneIgnore]
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
			OnPropertyChanged();
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion Quaternion
	{
		get => GetSimulatedTransform().Basis.GetRotationQuaternion();
		set
		{
			Quaternion q = value;
			if (!q.IsFinite()) return;
			SyncPhysicsTransform();
			GDNode3D.GlobalBasis = new(q);
			QueueTransformUpdate(AutoUpdateNetTransform, true);
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion LocalQuaternion
	{
		get => GetLocalTransform().Basis.GetRotationQuaternion();
		set
		{
			Quaternion q = value;
			if (!q.IsFinite()) return;
			SyncPhysicsTransform();
			GDNode3D.Basis = new(q);
			QueueTransformUpdate(AutoUpdateNetTransform, true);
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
			if (_netTransformAuthority != value)
			{
				ChangeNetTransformAuthority(value);
			}
			OnPropertyChanged();
		}
	}

	private void ChangeNetTransformAuthority(int value)
	{
		int previous = _netTransformAuthority;
		var network = Root?.Network;
		bool isLocal = network != null && value == network.LocalPeerID;
		bool wasLocal = network != null && previous == network.LocalPeerID;
		bool wasGaining = IsNetGaining;

		if (netMotion != null)
		{
			netMotion.StopGain();
			if (!isLocal)
			{
				netMotion.GainMotion = null;
			}
		}

		bool lost = wasLocal && !wasGaining && network != null && !network.IsServer && Root!.IsLoaded;
		Transform3D lostTransform = lost ? GetLocalTransform() : default;
		var lostVelocity = lost ? GetNetBodyVelocity() : default;

		_netTransformAuthority = value;
		PreviousNetTransformAuthority = previous;
		NetTransformAuthorityChangedAt = World.LocalClock;
		NetTransformAuthorityChangedNetTime = Root?.NetTime ?? 0;
		_writeAckUntil = double.MinValue;
		OnNetTransformAuthorityChanged();

		if (!wasGaining && !IsNetGaining && (network == null || wasLocal || isLocal))
		{
			netMotion?.Reset();
		}
		if (lost)
		{
			SeedNetSamples(lostTransform, lostVelocity.Linear, lostVelocity.Angular);
		}
	}

	// correct for godot would be -Z, but due to issues described in issue #369, we are using +Z except for the camera
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
		_pendingTransforms.Remove(this);
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
		_currentTransform = GetLocalTransform();
		_hasSyncedOnce = true;
		if (Root != null && Root.Network != null && Root.Network.IsServer)
		{
			_lastSentTransform = _currentTransform;
		}
		base.Ready();
	}

	private Transform3D? _lastSentTransform;
	private Transform3D? _lastTickTransform;
	private double _lastReliableMoveTime = double.MinValue;
	protected NetMotion? netMotion;
	private Transform3D _currentTransform;

	private Transform3D _lastNotifiedTransform;
	private bool _hasNotifiedOnce;

	/// <summary>
	/// Set if netwwork transform will be update automatically once setter called
	/// set this to false if you update them manually every frame via UpdateNetTransform()
	/// </summary>
	public bool AutoUpdateNetTransform { get; internal set; } = true;
	private bool _overrideNetworkTransform = false;

	/// <summary>
	/// Set to true if transform will be overrided, essentially ignoring network transform
	/// </summary>
	public bool OverrideNetworkTransform
	{
		get => _overrideNetworkTransform;
		internal set
		{
			if (value && !_overrideNetworkTransform)
			{
				_isDirty = false;
				netMotion?.Reset();
			}
			_overrideNetworkTransform = value;
		}
	}

	/// <summary>
	/// Virtual function to notify when node size changed
	/// </summary>
	/// <param name="newSize"></param>
	internal virtual void OnNodeSizeChanged(Vector3 newSize) { }

	internal int PreviousNetTransformAuthority { get; private set; } = 1;
	internal double NetTransformAuthorityChangedAt { get; private set; } = double.MinValue;
	internal double NetTransformAuthorityChangedNetTime { get; private set; }
	internal double LastServerWrite { get; private set; } = double.MinValue;
	internal byte ServerWriteSeq { get; private set; }
	internal byte AckedServerWrite { get; set; }
	private double _writeAckUntil = double.MinValue;
	internal bool HasPendingNetWrite => _pendingSyncToNet;

	internal virtual void OnNetTransformAuthorityChanged() { }

	internal bool IsAwaitingWriteAck(byte acked)
	{
		if (World.LocalClock >= _writeAckUntil) return false;
		if (acked != ServerWriteSeq) return true;
		_writeAckUntil = double.MinValue;
		return false;
	}

	protected virtual void SyncPhysicsTransform() { }

	protected virtual Transform3D GetSimulatedTransform() => GetGlobalTransform();

	protected virtual (Vector3 Linear, Vector3 Angular) GetNetBodyVelocity() => default;

	private void SeedNetSamples(Transform3D local, Vector3 linear, Vector3 angular)
	{
		Basis parent = GetNetParentBasis();

		Motion.Seed(local, parent.Inverse() * linear, parent.Orthonormalized().Inverse() * angular, Root.PhysicsNetTime);
		StartInterpolating();
	}

	protected void StartInterpolating()
	{
		_isDirty = true;
		SetPhysicsProcessWAuthor(true);
		Root.AddInterpolating(this);
	}

	internal (Vector3 Velocity, Vector3 AngularVelocity, double Time) GetNetMotion()
	{
		if (NetTransformAuthority == Root.Network.LocalPeerID || netMotion == null || netMotion.IsBridging || netMotion.Samples.Count == 0)
		{
			Vector3 velocity = this is Physical physical ? physical.Velocity : Vector3.Zero;
			return (velocity, GetNetBodyVelocity().Angular, Root.NetTime);
		}

		return netMotion.GetMotion();
	}

	internal NetMotion Motion => netMotion ??= new(this);

	internal virtual void FinishNetGain() { }

	internal bool IsNetGaining => netMotion != null && netMotion.IsGaining;

	internal float GetNetSpeed()
	{
		if (Root?.Network != null && NetTransformAuthority == Root.Network.LocalPeerID)
		{
			return this is Physical physical ? physical.Velocity.Length() : 0;
		}
		return netMotion?.Speed ?? 0;
	}

	internal double LastNetSampleTime => netMotion?.LastSampleTime ?? double.MinValue;

	internal Vector3 LimitNetVelocity(Vector3 position, double time, Vector3 velocity)
	{
		if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z)) return Vector3.Zero;

		float limit = NetVelocitySlack;
		if (netMotion != null && netMotion.Samples.Count > 0 && time > netMotion.Samples[netMotion.Samples.Count - 1].Time)
		{
			var last = netMotion.Samples[netMotion.Samples.Count - 1];
			limit += 2 * Mathf.Max(last.Velocity.Length(), last.Position.DistanceTo(position) / (float)(time - last.Time));
		}
		else if (this is Physical physical)
		{
			limit += 2 * physical.Velocity.Length();
		}
		return velocity.LimitLength(limit);
	}

	protected Vector3? GetNetSendVelocity(bool rest)
	{
		if (rest) return Vector3.Zero;
		if (this is not RigidBody body || body.IsFrozen) return null;

		return GetNetParentBasis().Inverse() * GetNetBodyVelocity().Linear;
	}

	internal void ApplyLatestNetSample()
	{
		if (netMotion == null || netMotion.Samples.Count == 0) return;

		var latest = netMotion.GetLatest();
		_currentTransform = new Transform3D(new Basis(latest.Rotation).ScaledLocal(GetLocalTransform().Basis.Scale), latest.Position);
		_oldLocalTransformApplied = default;
		SetLocalTransform(_currentTransform);
	}

	internal bool UpdateTransformTick(double delta)
	{
		if (!_isDirty) { return false; }

		Transform3D old = _currentTransform;

		UpdateTransform(delta);

		if (_currentTransform != old)
		{
			InvokeTransformChanged(old);
		}
		if (!_isDirty && this is not NPC { IsSitting: true })
		{
			SetPhysicsProcessWAuthor(false);
		}
		return _isDirty;
	}

	private void AddNetSample(TransformPayloadDto transform, double time, int period, bool rest, Vector3 velocity,
		bool snap = false, Vector3 spin = default)
	{
		if (!Motion.AddSample(transform, time, period, rest, velocity, snap, spin)) return;

		StartInterpolating();
	}

	internal bool TryGetNetVelocity(out Vector3 linear, out Vector3 angular)
	{
		if (netMotion == null || !netMotion.TryGetVelocity(out linear, out angular))
		{
			linear = Vector3.Zero;
			angular = Vector3.Zero;
			return false;
		}

		Basis parent = GetNetParentBasis();
		linear = parent * linear;
		angular = parent.Orthonormalized() * angular;
		return true;
	}

	protected bool IsNetRestSent(Transform3D current) => netMotion?.RestTransform is Transform3D sent && sent.IsEqualApprox(current);

	internal Basis GetNetParentBasis()
	{
		return GDNode3D.GetParentNode3D() is Node3D node && node.IsInsideTree() ? node.GlobalBasis : Basis.Identity;
	}

	private void UpdateTransform(double delta)
	{
		if (IsNetGaining && (netMotion!.Samples.Count == 0 || netMotion.GainElapsed >= netMotion.GainTime + NetworkTransformSync.TickInterval))
		{
			FinishNetGain();
		}

		// If has transform authority, no need to update yourself
		if ((NetTransformAuthority == Root.Network.LocalPeerID && !IsNetGaining) || netMotion == null || netMotion.Samples.Count == 0)
		{
			_isDirty = false;
			return;
		}

		double renderTime = netMotion.UpdateRenderTime(delta);
		if (!netMotion.Interpolate(renderTime, delta, _isFirstUpdate, out Vector3 position, out Quaternion rotation, out bool settled)) return;
		if (settled) _isDirty = false;

		_isFirstUpdate = false;
		_currentTransform = new Transform3D(new Basis(rotation).ScaledLocal(GetLocalTransform().Basis.Scale), position);

		if (this is Part)
		{
			Transform3D setto = new(new Basis(rotation), position);
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

		if (IsNetGaining && netMotion.GainElapsed >= netMotion.GainTime)
		{
			FinishNetGain();
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

		SyncPhysicsTransform();
		Vector3 lookTarget = pos;

		// Godot's LookAt points at -Z, Polytoria uses +Z as forward
		if (this is not Camera)
		{
			Vector3 origin = GDNode3D.GlobalPosition;
			lookTarget = origin - (pos - origin);
		}

		GDNode3D.LookAt(lookTarget, up);

		QueueTransformUpdate(true, true);
	}

	[ScriptMethod]
	public void Translate(Vector3 translation)
	{
		SyncPhysicsTransform();
		SetGlobalTransformRaw(GetGlobalTransform().Translated(translation));
		QueueTransformUpdate(AutoUpdateNetTransform, true);
	}

	private void RotateAroundBasis(Vector3 point, Basis rotation)
	{
		SyncPhysicsTransform();
		Transform3D transform = GetGlobalTransform();

		transform.Origin -= point;

		transform.Basis = rotation * transform.Basis;
		transform.Origin = rotation.Xform(transform.Origin);

		transform.Origin += point;

		SetGlobalTransformRaw(transform);
		QueueTransformUpdate(AutoUpdateNetTransform, true);
	}

	[ScriptMethod]
	public void RotateAround(Vector3 point, Vector3 axis, float angle)
	{
		RotateAroundBasis(point, new(axis.Normalized(), Mathf.DegToRad(angle)));
	}

	[ScriptMethod]
	public void RotateAround(Vector3 point, Quaternion q)
	{
		RotateAroundBasis(point, new(q));
	}

	[ScriptMethod]
	public void Rotate(Vector3 eulerAngles)
	{
		Vector3 radians = eulerAngles * Mathf.DegToRad(1.0f);
		SyncPhysicsTransform();

		GDNode3D.RotateObjectLocal(Vector3.Right, radians.X);
		GDNode3D.RotateObjectLocal(Vector3.Up, radians.Y);
		GDNode3D.RotateObjectLocal(Vector3.Back, radians.Z);

		QueueTransformUpdate(AutoUpdateNetTransform, true);
	}

	[ScriptMethod]
	public void Rotate(Quaternion q)
	{
		GDNode3D.Basis = new Basis(q) * GDNode3D.Basis;
		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	// NOTE: Update operations needs transform to be force updated as godot does not update them instantly
	protected void UpdateNetTransform()
	{
		if (Root == null || Root.Network == null) return;

		GDNode3D.ForceUpdateTransform();
		Transform3D current = GetLocalTransform();
		bool teleport = _netTeleport;
		_netTeleport = false;
		bool stepped = _lastTickTransform is Transform3D lastTick && HasChangedForNetworkSync(lastTick, current, NetStillStep);
		_lastTickTransform = current;

		if (_lastSentTransform is Transform3D lastSent)
		{
			if (!HasChangedForNetworkSync(lastSent, current))
			{
				if (netMotion != null && netMotion.IsMoving && Root.IsLoaded && !stepped &&
					!(GetNetBodyVelocity().Linear.Length() > NetStillSpeed))
				{
					if (World.PhysicsClock - netMotion.LastMoveTime >= NetRestDelay)
					{
						netMotion.IsMoving = false;
						netMotion.RestTransform = current;
						SendNetTransformUnreliable(current, true, true);
					}
					else if (!netMotion.IsStopSent)
					{
						netMotion.IsStopSent = true;
						SendNetTransformUnreliable(current, velocity: Vector3.Zero);
					}
				}
				return;
			}
		}

		_lastSentTransform = current;
		NetMotion net = Motion;
		net.IsMoving = true;
		net.IsStopSent = false;
		net.LastMoveTime = World.PhysicsClock;
		net.RestTransform = null;

		UpdateCurrentTransformCache(current);
		if (!Root.IsLoaded || IsDeleted) return;
		SendNetTransformUnreliable(current, !teleport);
	}

	internal void QueueTransformUpdate(bool syncToNet = false, bool scripted = false)
	{
		ForceUpdateTransform();
		if (scripted) MarkScriptMove();
		_pendingSyncToNet |= syncToNet;
		_pendingTransforms.Add(this);
	}

	private void MarkScriptMove()
	{
		double now = World.LocalClock;
		if (this is RigidBody { IsFrozen: false } && Root?.Network != null &&
			NetTransformAuthority == Root.Network.LocalPeerID && now - _lastScriptMove >= NetMotion.MaxGap)
		{
			_netTeleport = true;
		}
		_lastScriptMove = now;
	}

	internal static void FlushTransformUpdates()
	{
		if (_pendingTransforms.Count == 0) return;

		Dynamic[] pending = [.. _pendingTransforms];
		_pendingTransforms.Clear();

		foreach (Dynamic dyn in pending)
		{
			try
			{
				dyn.CommitTransformUpdate();
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}
	}

	private void CommitTransformUpdate()
	{
		bool syncToNet = _pendingSyncToNet;
		_pendingSyncToNet = false;

		if (IsDeleted || !Node.IsInstanceValid(GDNode3D) || !GDNode3D.IsInsideTree()) return;

		Transform3D current = GetLocalTransform();
		UpdateCurrentTransformCache(current);
		if (!syncToNet || Root?.Network == null || IsDeleted || _lastSentTransform?.IsEqualApprox(current) == true) return;

		_lastSentTransform = current;

		double now = World.LocalClock;
		bool moving = now - _lastReliableMoveTime < NetMotion.MaxGap;
		_lastReliableMoveTime = now;
		bool owned = NetTransformAuthority == Root.Network.LocalPeerID;
		if (!moving && !owned && Root.Network.IsServer)
		{
			LastServerWrite = Root.NetTimeNow;
			ServerWriteSeq = (byte)(ServerWriteSeq % 255 + 1);
			_writeAckUntil = now + NetWriteAckTimeout;
		}

		if (!Root.IsLoaded) return;
		if (moving && Root.Network.IsServer && owned)
		{
			SendNetTransformUnreliable(current);
			Root.Network.TransformSync.AddScriptedMover(this);
			return;
		}
		SendNetTransformReliable(current, moving && owned);
	}

	internal bool SendScriptedRest()
	{
		if (IsDeleted) return true;
		if (World.LocalClock - _lastReliableMoveTime < NetRestDelay) return false;
		if (Root.IsLoaded) SendNetTransformReliable(GetLocalTransform(), true, true);
		return true;
	}

	protected void SendNetTransformUnreliable(bool lerp = true, bool rest = false)
	{
		if (Root == null || Root?.Network == null) { return; }

		ForceUpdateTransform();
		Transform3D current = GetLocalTransform();
		UpdateCurrentTransformCache(current);
		if (IsDeleted) return;
		SendNetTransformUnreliable(current, lerp, rest);
	}

	private void SendNetTransformUnreliable(Transform3D current, bool lerp = true, bool rest = false, Vector3? velocity = null)
	{
		TransformPayloadDto payload = TransformPayloadDto.FromGDTransform(current);
		velocity ??= GetNetSendVelocity(rest);

		if (!Root.Network.IsServer)
		{
			// Send transform to server
			Root.Network.TransformSync.SendTransformToServer(this, payload, lerp, rest, velocity);
		}
		else
		{
			// Server broadcasts to all clients
			Root.Network.TransformSync.BroadcastTransformFromServer(this, payload, lerp, reliable: rest, rest: rest, velocity: velocity);
		}
	}

	protected void SendNetTransformReliable(bool lerp = false, bool rest = false)
	{
		if (Root == null || Root?.Network == null) return;
		_isDirty = false;

		ForceUpdateTransform();
		Transform3D current = GetLocalTransform();
		UpdateCurrentTransformCache(current);
		if (IsDeleted) return;
		SendNetTransformReliable(current, lerp, rest);
	}

	private void SendNetTransformReliable(Transform3D current, bool lerp = false, bool rest = false)
	{
		ReliableTransformChanged?.Invoke();

		if (Root.Network.IsServer)
		{
			TransformPayloadDto payload = TransformPayloadDto.FromGDTransform(current);
			Root.Network.TransformSync.BroadcastTransformFromServer(this, payload, lerp,
				reliable: true, rest: rest, velocity: GetNetSendVelocity(rest));
		}

		// Cannot broadcast as reliable in client, values are ignored in client
	}

	private void UpdateCurrentTransformCache(Transform3D newt)
	{
		if (newt != _currentTransform)
		{
			if (_hasSyncedOnce)
			{
				InvokeTransformChanged(_currentTransform, newt);
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
	internal virtual TransformPayloadDto TransformNetworkPass(int fromPeer, TransformPayloadDto newTransform)
	{
		return newTransform;
	}

	internal virtual bool TransformNetworkCheck(TransformPayloadDto newTransform)
	{
		return true;
	}

	internal void UpdateTransformFromNet(TransformPayloadDto transform, bool isReliable, bool lerpTransform,
		double time = 0, int period = 1, bool rest = false, Vector3 velocity = default, Vector3 spin = default)
	{
		if (OverrideNetworkTransform) return;
		if (!lerpTransform && time > 0 && !Root.Network.IsServer && time < LastNetSampleTime &&
			(netMotion == null || !netMotion.IsProvisional))
		{
			return;
		}
		if (IsNetGaining && !(lerpTransform && time > 0))
		{
			FinishNetGain();
		}
		if (time > 0 && !Root.Network.IsServer &&
			(lerpTransform || (netMotion != null && netMotion.IsAhead(time) && NetTransformAuthority != Root.Network.LocalPeerID)))
		{
			AddNetSample(transform, time, period, rest, velocity, !lerpTransform, spin);
			if (isReliable)
			{
				ReliableTransformChanged?.Invoke();
			}
			return;
		}

		if (lerpTransform && time > 0)
		{
			Motion.KeepSample(transform, time, rest, velocity);
		}
		else if (time > 0 && !Root.Network.IsServer)
		{
			Motion.SetSample(transform, time, velocity);
		}
		else
		{
			netMotion?.Reset();
		}
		Transform3D local = GetLocalTransform();
		Vector3 scale = local.Basis.Scale;
		Transform3D netTransform = new(
			new Basis(transform.Rotation).ScaledLocal(scale),
			transform.Position
		);

		if (Root.Network.IsServer)
		{
			_lastSentTransform = netTransform;
		}

		// TODO: SetPhysicsProcess affects Physical.cs's tick, but could also affect other behaviour.
		// Maybe we need to make something dedicated to physical tick?
		_isDirty = false;
		SetPhysicsProcessWAuthor(false);

		if (_hasSyncedOnce && netTransform.IsEqualApprox(local))
		{
			if (isReliable)
			{
				ReliableTransformChanged?.Invoke();
			}
			return;
		}

		_oldLocalTransformApplied = default;
		SetLocalTransform(netTransform);
		if (NetTransformAuthority == Root.Network.LocalPeerID && this is RigidBody body)
		{
			body.WakeNetBody();
		}

		if (isReliable)
		{
			ReliableTransformChanged?.Invoke();
		}
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
		_boundArea3D.CollisionLayer =
			to && this is not IGroup and not Camera and not Physical
				? 1 << 2
				: 0u;
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

	internal void InvokeTransformChanged(Transform3D? previous = null, Transform3D? newTransform = null)
	{
#if CREATOR
		if (Root.SessionType == World.SessionTypeEnum.Creator && Root.CreatorContext != null && Root.CreatorContext.Gizmos != null)
		{
			if (!Root.CreatorContext.Gizmos.HoveringGizmos && !Root.CreatorContext.Gizmos.IsDraggingDynamic)
			{
				// Update creator bounds if not changed by gizmos
				UpdateCreatorBounds();
			}
		}
#endif

		bool moved = true;
		bool rotated = true;
		bool resized = true;

		Transform3D current = newTransform ?? GetLocalTransform();

		if (previous != null && _hasNotifiedOnce)
		{
			bool sameBasis = _lastNotifiedTransform.Basis == current.Basis;
			moved = _lastNotifiedTransform.Origin.DistanceTo(current.Origin) > 0.001f;
			rotated = !sameBasis && _lastNotifiedTransform.Basis.GetRotationQuaternion().AngleTo(current.Basis.GetRotationQuaternion()) > 0.001f;
			resized = !sameBasis && (_lastNotifiedTransform.Basis.Scale - current.Basis.Scale).Length() > 0.001f;
		}

		// Notify transform change without sync to clients
		if (moved)
		{
			OnPropertyChanged(nameof(Position), false);
			OnPropertyChanged(nameof(LocalPosition), false);
		}
		if (rotated)
		{
			OnPropertyChanged(nameof(Rotation), false);
			OnPropertyChanged(nameof(LocalRotation), false);
			OnPropertyChanged(nameof(Quaternion), false);
			OnPropertyChanged(nameof(LocalQuaternion), false);
		}
		if (resized)
		{
			OnPropertyChanged(nameof(Size), false);
			OnPropertyChanged(nameof(LocalSize), false);
		}

		if (moved || rotated || resized)
		{
			_lastNotifiedTransform = current;
			_hasNotifiedOnce = true;
		}

		TransformChanged?.Invoke();
		if (Children.Count > 0)
		{
			foreach (Instance item in GetChildren())
			{
				if (item is Dynamic dyn)
				{
					dyn.InvokeTransformChanged();
				}
			}
		}

		// Destroy entity/rigidbodies under part destroy height
		if (this is Physical { Anchored: false } && Root != null && Root.Environment != null)
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
		OnPropertyChanged(nameof(LocalSize), false);
		OnPropertyChanged(nameof(Size), false);
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
	}

	internal Vector3 GetLocalPosition()
	{
		return GetLocalTransform().Origin / GetParentScale();
	}

	internal void SetLocalPosition(Vector3 to)
	{
		var t = GetLocalTransform();
		SetLocalTransformRaw(new Transform3D(t.Basis, to * GetParentScale()));
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
		if (!oldN.IsEqualApprox(NodeSize))
		{
			PropagateParentSizeChanged(oldN);
		}
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
		QueueTransformUpdate();
	}

	internal void SetLocalTransform(Transform3D to)
	{
		SetLocalTransformRaw(to);
		QueueTransformUpdate();
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

		target.QueueTransformUpdate();
	}

	protected bool HasChangedForNetworkSync(Transform3D a, Transform3D b, float scale = 1)
	{
		float dst = a.Origin.DistanceTo(b.Origin);
		if (dst > PositionSyncThreshold * scale)
		{
			return true;
		}

		Quaternion aRot = a.Basis.GetRotationQuaternion().Normalized();
		Quaternion bRot = b.Basis.GetRotationQuaternion().Normalized();

		float deg = Mathf.RadToDeg(aRot.AngleTo(bRot));
		if (deg > RotationSyncThreshold * scale)
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
				Aabb pBounds = part.GetSelfBound();

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
					if (n is VisibleOnScreenNotifier3D) continue;
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

	public virtual Aabb GetSelfBound() { return new(GetGlobalPosition(), Vector3.Zero); }
}
