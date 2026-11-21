// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class RigidBody : Physical
{
	private static readonly Dictionary<(float, float), PhysicsMaterial> _physicsMaterials = [];
	internal RigidBody3D GDRigidBody = null!;
	private PhysicsMaterial? _appliedPhysicsMaterial;

	private float _gravityScale = 1f;
	private bool _lockRotation = false;
	private float _mass = 1f;
	private float _friction = 0.6f;
	private float _drag;
	private float _angularDrag;
	private float _bounciness;
	private Vector3 _frozenVelocity;
	private Vector3 _frozenAngularVelocity;
	private double _frozenVelocityTime = double.MinValue;
	private double _frozenAngularVelocityTime = double.MinValue;

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 Velocity
	{
		get
		{
			if (!IsFrozen) return GDRigidBody.LinearVelocity;
			if (!HasOwnerVelocity(_frozenVelocityTime)) return _frozenVelocity;
			return GetNetParentBasis() * GetNetMotion().Velocity;
		}
		set
		{
			if (!IsApplyingNetProp) PrepareForce();
			_frozenVelocity = value;
			_frozenVelocityTime = Root?.NetTime ?? double.MinValue;
			if (!IsFrozen || GDRigidBody.LinearVelocity != value)
			{
				GDRigidBody.LinearVelocity = value;
			}
			OnPropertyChanged(nameof(Velocity), !IsServerSimulated);
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 AngularVelocity
	{
		get
		{
			if (!IsFrozen) return GDRigidBody.AngularVelocity.FlipEuler();
			if (!HasOwnerVelocity(_frozenAngularVelocityTime)) return _frozenAngularVelocity;
			return (GetNetParentBasis().Orthonormalized() * GetNetMotion().AngularVelocity).FlipEuler();
		}
		set
		{
			if (!IsApplyingNetProp) PrepareForce();
			_frozenAngularVelocity = value;
			_frozenAngularVelocityTime = Root?.NetTime ?? double.MinValue;
			Vector3 v = value.FlipEuler();
			if (!IsFrozen || GDRigidBody.AngularVelocity != v)
			{
				GDRigidBody.AngularVelocity = v;
			}
			OnPropertyChanged(nameof(AngularVelocity), !IsServerSimulated);
		}
	}

	private bool IsServerSimulated => !IsFrozen && Root?.Network?.IsServer == true && NetTransformAuthority == 1;

	private bool HasOwnerVelocity(double after)
	{
		if (Root == null || Root.Network == null || NetTransformAuthority == Root.Network.LocalPeerID) return false;
		if (IsNetBridging || LastNetSampleTime <= after) return false;
		return true;
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float GravityScale
	{
		get => _gravityScale;
		set
		{
			if (_gravityScale == value)
			{
				return;
			}

			_gravityScale = value;

			GDRigidBody.GravityScale = value * 2f;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use GravityScale instead"), CloneIgnore, SaveIgnore]
	public bool UseGravity
	{
		get => GravityScale != 0f;
		set
		{
			if (UseGravity == value)
			{
				return;
			}

			GravityScale = value ? 1f : 0f;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Mass
	{
		get => _mass;
		set
		{
			if (_mass == value)
			{
				return;
			}

			_mass = value;

			GDRigidBody.Mass = Math.Max(_mass, Physical.MinMass);

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.6f)]
	public float Friction
	{
		get => _friction;
		set
		{
			if (_friction == value)
			{
				return;
			}

			_friction = value;
			UpdatePhysicsMaterial();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float Drag
	{
		get => _drag;
		set
		{
			if (_drag == value)
			{
				return;
			}

			_drag = value;
			GDRigidBody.LinearDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float AngularDrag
	{
		get => _angularDrag;
		set
		{
			if (_angularDrag == value)
			{
				return;
			}

			_angularDrag = value;
			GDRigidBody.AngularDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float Bounciness
	{
		get => _bounciness;
		set
		{
			if (_bounciness == value)
			{
				return;
			}

			_bounciness = value;

			UpdatePhysicsMaterial();

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool LockRotation
	{
		get => _lockRotation;
		set
		{
			if (_lockRotation == value)
			{
				return;
			}

			_lockRotation = value;

			GDRigidBody.LockRotation = value;

			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return new RigidBody3D();
	}

	public override void InitGDNode()
	{
		base.InitGDNode();
		RigidBody3D? previous = GDRigidBody;
		GDRigidBody = (RigidBody3D)GDNode;
		_appliedPhysicsMaterial = null;
		UpdatePhysicsMaterial();
		GDRigidBody.GravityScale = 2;

		if (ReferenceEquals(previous, GDRigidBody)) return;

		if (previous != null)
		{
			previous.SleepingStateChanged -= OnSleepingStateChanged;
		}

		GDRigidBody.SleepingStateChanged += OnSleepingStateChanged;
	}

	private void UpdatePhysicsMaterial()
	{
		if (!_physicsMaterials.TryGetValue((_friction, _bounciness), out PhysicsMaterial? mat))
		{
			mat = new() { Friction = _friction, Bounce = _bounciness };
			_physicsMaterials[(_friction, _bounciness)] = mat;
		}
		if (ReferenceEquals(_appliedPhysicsMaterial, mat)) return;
		_appliedPhysicsMaterial = mat;
		GDRigidBody.PhysicsMaterialOverride = mat;
	}

	public override void Init()
	{
		base.Init();
		Anchored = true;
		CanCollide = true;
	}

	private void OnSleepingStateChanged()
	{
		if (IsAsleep && !IsNetRestSent(GetLocalTransform()))
		{
			SendRestNetTransform();
		}

		UpdatePhysicsTick();
	}

	internal override void ApplyAddForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			GDRigidBody.ApplyCentralForce(force);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			GDRigidBody.AddConstantCentralForce(force);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			GDRigidBody.ApplyCentralImpulse(force);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddTorque(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			GDRigidBody.ApplyTorque(force);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			GDRigidBody.AddConstantTorque(force);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			GDRigidBody.ApplyTorqueImpulse(force);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			AngularVelocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddForceAtPosition(Vector3 force, Vector3 position, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			GDRigidBody.ApplyForce(force, position);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			GDRigidBody.AddConstantForce(force, position);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			GDRigidBody.ApplyImpulse(force, position);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddRelativeForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 worldForce = GDRigidBody.GlobalTransform.Basis * force;
		if (mode == ForceModeEnum.Force)
		{
			GDRigidBody.ApplyCentralForce(worldForce);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			GDRigidBody.AddConstantCentralForce(worldForce);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			GDRigidBody.ApplyCentralImpulse(worldForce);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			GDRigidBody.LinearVelocity += worldForce;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddRelativeTorque(Vector3 torque, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 worldTorque = GDRigidBody.GlobalTransform.Basis * torque;

		if (mode == ForceModeEnum.Force)
		{
			GDRigidBody.ApplyTorque(worldTorque);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			GDRigidBody.AddConstantTorque(worldTorque);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			GDRigidBody.ApplyTorqueImpulse(worldTorque);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			GDRigidBody.AngularVelocity += worldTorque;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	protected override void ApplyFreeze(bool to)
	{
		if (to && !GDRigidBody.Freeze)
		{
			_frozenVelocity = GDRigidBody.LinearVelocity;
			_frozenAngularVelocity = GDRigidBody.AngularVelocity.FlipEuler();
			_frozenVelocityTime = Root?.NetTime ?? double.MinValue;
			_frozenAngularVelocityTime = _frozenVelocityTime;
			if (GDRigidBody.IsInsideTree())
			{
				PhysicsServer3D.BodySetState(GDRigidBody.GetRid(), PhysicsServer3D.BodyState.Transform, GDRigidBody.GlobalTransform);
			}
		}
		GDRigidBody.Freeze = to;
		base.ApplyFreeze(to);
	}

	internal void WakeNetBody()
	{
		if (!IsFrozen)
		{
			GDRigidBody.Sleeping = false;
			UpdatePhysicsTick();
		}
	}

	protected override void SyncPhysicsTransform()
	{
		if (IsFrozen || !GDRigidBody.IsInsideTree()) return;
		GDRigidBody.SetIgnoreTransformNotification(true);
		GDRigidBody.GlobalTransform = PhysicsServer3D.BodyGetState(GDRigidBody.GetRid(), PhysicsServer3D.BodyState.Transform).AsTransform3D();
		GDRigidBody.SetIgnoreTransformNotification(false);
	}

	protected override Transform3D GetSimulatedTransform()
	{
		if (IsFrozen || !GDRigidBody.IsInsideTree()) return GetGlobalTransform();
		return PhysicsServer3D.BodyGetState(GDRigidBody.GetRid(), PhysicsServer3D.BodyState.Transform).AsTransform3D();
	}

	protected override (Vector3 Linear, Vector3 Angular) GetNetBodyVelocity()
	{
		if (IsFrozen) return default;
		return (GDRigidBody.LinearVelocity, GDRigidBody.AngularVelocity);
	}

	protected override void ApplyNetVelocity(Vector3 linear, Vector3 angular)
	{
		if (IsFrozen) return;
		GDRigidBody.LinearVelocity = linear;
		GDRigidBody.AngularVelocity = angular;
	}

	internal override bool IsAsleep => !GDRigidBody.Freeze && GDRigidBody.Sleeping;
	internal override bool IsFrozen => GDRigidBody.Freeze;
	internal bool HasConstantForce => GDRigidBody.ConstantForce != Vector3.Zero || GDRigidBody.ConstantTorque != Vector3.Zero;
}
