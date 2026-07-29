// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Folder : Instance, IGroup
{
	public override Node CreateGDNode()
	{
		// Keep the spatial transform chain intact for Dynamic descendants.
		return new Node3D();
	}
}
