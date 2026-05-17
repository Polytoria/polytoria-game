// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Shared;

public partial class LoadingGuy : Control
{
	private PolytorianModel _pt = null!;

	public override void _Ready()
	{
		_pt = new();
		GetNode("SubViewport").AddChild(_pt.GDNode);
		_pt.InitEntry();
		_pt.Position = new(0, 0, -10);
		_pt.Rotation = new(0, 90, 0);
		_pt.PlayRun();
	}

	public override void _ExitTree()
	{
		_pt.Delete();
		base._ExitTree();
	}
}
