// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Static]
public sealed partial class SunLight : Light
{
#if CREATOR
	private ArrowSpatial _arrow = null!;
#endif

	public override Node CreateGDNode()
	{
		Node3D n = new();
		DirectionalLight3D sl = new() { DirectionalShadowMaxDistance = 1000f };
		n.AddChild(sl, @internal: Node.InternalMode.Back);
		sl.RotationDegrees = new(0, 180, 0); // Facing Z+
		GDLight = sl;
		return n;
	}

	public override void Init()
	{
		base.Init();

		DirectionalLight3D directionLight = (DirectionalLight3D)GDLight;

#if GODOT_MOBILE
		directionLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
#endif

#if CREATOR
		GDNode.AddChild(_arrow = new() { Visible = false }, @internal: Node.InternalMode.Back);
#endif
	}

	public override void InitOverrides()
	{
		// Default sun properties
		Brightness = 1;
		Color = Color.FromString("#FFF4D6", new());
		Shadows = true;
		Position = new(0, 15, 0);
		Rotation = new(50, -330, 0);
		base.InitOverrides();
	}

#if CREATOR
	public override void CreatorSelected()
	{
		_arrow.Visible = true;
		base.CreatorSelected();
	}

	public override void CreatorDeselected()
	{
		_arrow.Visible = false;
		base.CreatorDeselected();
	}
#endif
}
