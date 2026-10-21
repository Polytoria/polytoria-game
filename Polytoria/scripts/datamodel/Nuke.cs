// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Nuke : Explosion
{
	public Nuke()
	{
		// EXPLODE
		Radius = Force = Damage = float.PositiveInfinity;
		AffectAnchored = AffectWelds = UseEffects = true;

		// BURN THEM ALL
		Touched.Connect<Instance>(i =>
		{
			if (i is Entity e)
			{
				e.Color = Colors.Black;
			}
		});

		// crash for dramatic effect
		OS.Crash("Exploded by Nuke");
	}
}
