// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Utils;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class RigidBody : Physical
{
	internal RigidBody3D GDRigidBody = null!;
	internal PhysicsMaterial PhysicsMat = null!;

	private bool _useGravity = true;
	private bool _lockRotation = false;
	private float _mass;
	private float _friction;
	private float _drag;
	private float _angularDrag;
	private float _bounciness;

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 Velocity
	{
		get
		{
			return GDRigidBody.LinearVelocity;
		}
		set
		{
			GDRigidBody.LinearVelocity = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 AngularVelocity
	{
		get
		{
			return GDRigidBody.AngularVelocity.FlipEuler();
		}
		set
		{
			GDRigidBody.AngularVelocity = value.FlipEuler();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool UseGravity
	{
		get => _useGravity;
		set
		{
			if (_useGravity == value)
			{
				return;
			}

			_useGravity = value;

			GDRigidBody.GravityScale = value ? 2 : 0;

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
			PhysicsMat.Friction = value;
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

			PhysicsMat.Bounce = value;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool LockRotation
	{
		get => _lockRotation;
		set
		{
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
		PhysicsMat = new();
		GDRigidBody = (RigidBody3D)GDNode;
		GDRigidBody.PhysicsMaterialOverride = PhysicsMat;
		GDRigidBody.GravityScale = 2;
	}

	public override void Init()
	{
		base.Init();
		Anchored = true;
		CanCollide = true;
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
		GDRigidBody.Freeze = to;
		base.ApplyFreeze(to);
	}
}
