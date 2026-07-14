// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BodyForce : Instance
{
	private Vector3 _force = Vector3.Zero;

	[Editable, ScriptProperty]
	public Vector3 Force
	{
		get => _force;
		set
		{
			_force = value;
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
		if (Parent != null)
		{
			if (Parent is NPC npc)
			{
				npc.CharacterVelocity += Force * (float)delta;
			}
			else if (Parent.GDNode is RigidBody3D rigid3D)
			{
				rigid3D.ApplyCentralForce(Force);
			}
		}

		base.PhysicsProcess(delta);
	}
}
