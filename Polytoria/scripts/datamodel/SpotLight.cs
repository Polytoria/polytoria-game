// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Creator.Spatial;
#endif

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class SpotLight : Light
{
	internal SpotLight3D GDSpotLight = null!;
	private float _range = 30;
	private float _angle = 30;
#if CREATOR
	private ConeSpatial _cone = null!;
#endif

	public override Node CreateGDNode()
	{
		Node3D n = new();
		SpotLight3D sl = new();
		n.AddChild(sl, @internal: Node.InternalMode.Back);
		sl.RotationDegrees = new(0, 180, 0); // Facing Z+
		GDLight = sl;
		GDSpotLight = sl;
		return n;
	}

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(_cone = new() { Visible = false }, @internal: Node.InternalMode.Back);
#endif

		base.Init();
	}

	public override void InitOverrides()
	{
		Range = 30;
		Angle = 30;
		base.InitOverrides();
	}

	[Editable, ScriptProperty]
	public float Range
	{
		get => _range;
		set
		{
			_range = value;
			GDSpotLight.SpotRange = value;
#if CREATOR
			_cone.Range = value;
#endif
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Angle
	{
		get => _angle;
		set
		{
			_angle = value;
			GDSpotLight.SpotAngle = value;
#if CREATOR
			_cone.Angle = value;
#endif
			OnPropertyChanged();
		}
	}

#if CREATOR
	public override void CreatorSelected()
	{
		_cone.Visible = true;
		base.CreatorSelected();
	}

	public override void CreatorDeselected()
	{
		_cone.Visible = false;
		base.CreatorDeselected();
	}
#endif
}
