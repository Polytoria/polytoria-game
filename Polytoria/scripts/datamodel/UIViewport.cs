// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIViewport : UIField
{
	private SubViewport _subViewport = null!;
	private WorldEnvironment _worldEnv = null!;

	public override Node CreateGDNode()
	{
		SubViewportContainer container = new() { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new(32, 32) };
		_subViewport = new() { HandleInputLocally = false, TransparentBg = true, OwnWorld3D = true };

		_worldEnv = new();
		_subViewport.AddChild(_worldEnv);

		container.Stretch = true;
		container.AddChild(_subViewport);
		return container;
	}

	public override void InitGDNode()
	{
		SlotNode = _subViewport;
		base.InitGDNode();
	}

	public override void Init()
	{
		_worldEnv.Environment = Root.Lighting.environment;
		base.Init();
	}
}
