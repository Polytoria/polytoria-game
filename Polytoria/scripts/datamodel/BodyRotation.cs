// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BodyRotation : Instance
{
	private Vector3 _targetRotation = new(0, 0, 0);
	private float _force = 0;
	private float _acceptanceAngle = 5;

	[Editable, ScriptProperty]
	public Vector3 TargetRotation
	{
		get => _targetRotation;
		set
		{
			_targetRotation = value;
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
		Quaternion gdRot = Quaternion.FromEuler(_targetRotation);

		if (Parent != null && Parent.GDNode is RigidBody3D rigid3D)
		{
			Vector3 currentPos = rigid3D.GlobalPosition;
			Quaternion currentRot = rigid3D.GlobalBasis.GetRotationQuaternion();
			Quaternion error = gdRot * currentRot.Inverse();

			if (error.W < 0)
			{
				error = -error;
			}

			error = error.Normalized();
			Vector3 axis = error.GetAxis();
			float angle = error.GetAngle();
			float speed = Mathf.Min(angle * Force, Force);

			if (angle > Mathf.DegToRad(AcceptanceAngle))
			{
				rigid3D.AngularVelocity = axis * speed;
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
		_targetRotation = quaternion.GetEuler();
	}
}
