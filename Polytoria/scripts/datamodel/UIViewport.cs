// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIViewport : UIField
{
	internal MultiPassView SubView = null!;
	private SubViewport _initialView = null!;
	private WorldEnvironment _worldEnv = null!;

	public override Node CreateGDNode()
	{
		_initialView = new() { HandleInputLocally = false, TransparentBg = true, OwnWorld3D = true };
		SubView = new(_initialView) { FocusMode = Control.FocusModeEnum.None };

		_worldEnv = new();
		_initialView.AddChild(_worldEnv);
		SubView.AddChild(_initialView);
		return SubView;
	}

	public override void InitGDNode()
	{
		SlotNode = _initialView;
		base.InitGDNode();
	}

	public override void Init()
	{
		_worldEnv.Environment = Root.Lighting.environment;
		base.Init();
	}
}
