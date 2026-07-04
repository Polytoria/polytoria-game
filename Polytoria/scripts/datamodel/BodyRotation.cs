// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Utils;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BodyRotation : Instance
{
	private Quaternion _targetQuaternion = Quaternion.Identity;
	private float _force = 0;
	private float _acceptanceAngle = 5;

	[Editable, ScriptProperty]
	public Vector3 TargetRotation
	{
		get => _targetQuaternion.GetEuler().RadToDeg();
		set
		{
			_targetQuaternion = Quaternion.FromEuler(value.DegToRad());
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float Force
	{
		get => _force;
		set
		{
			_force = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(5)]
	public float AcceptanceAngle
	{
		get => _acceptanceAngle;
		set
		{
			_acceptanceAngle = value;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		SetPhysicsProcess(true);
		base.Init();
	}

	public override void PhysicsProcess(double delta)
	{
		if (Parent != null && Parent.GDNode is RigidBody3D rigid3D)
		{
			Quaternion error = _targetQuaternion * rigid3D.GlobalBasis.GetRotationQuaternion().Inverse();

			if (error.W < 0)
			{
				error = -error;
			}

			float angle = error.GetAngle();

			if (angle > Mathf.DegToRad(AcceptanceAngle))
			{
				rigid3D.AngularVelocity = error.GetAxis() * Mathf.Min(angle * Force, Force);
			}
			else
			{
				rigid3D.AngularVelocity = Vector3.Zero;
			}
		}
		base.PhysicsProcess(delta);
	}

	[ScriptMethod]
	public void SetQuaternion(Quaternion quaternion)
	{
		_targetQuaternion = quaternion;
	}
}
