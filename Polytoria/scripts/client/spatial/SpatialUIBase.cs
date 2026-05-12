// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI;

public partial class SpatialUIBase : Sprite3D
{
	public const float SpatialVisibleRange = 40;

	public override void _Process(double delta)
	{
		if (World.Current == null) { Visible = false; return; }
		Camera? cam = World.Current!.Environment.CurrentCamera;

		if (cam != null)
		{
			Visible = (cam.Position - GlobalPosition).Length() < SpatialVisibleRange;
		}
	}
}
