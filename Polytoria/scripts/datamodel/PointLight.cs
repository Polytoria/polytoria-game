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
public sealed partial class PointLight : Light
{
	internal OmniLight3D GDOmniLight = null!;
	private const float RangeConversion = 3.0f;
	private float _range;
#if CREATOR
	private SphereSpatial _sphere = null!;
#endif

	public override Node CreateGDNode()
	{
		Node3D n = new();
		OmniLight3D sl = new();
		n.AddChild(sl, @internal: Node.InternalMode.Back);
		GDLight = sl;
		GDOmniLight = sl;
		return n;
	}

	public override void Init()
	{
#if CREATOR
		GDNode.AddChild(_sphere = new() { Visible = false }, @internal: Node.InternalMode.Back);
#endif
		base.Init();
	}

	public override void InitOverrides()
	{
		Range = 30;
		base.InitOverrides();
	}

	[Editable, ScriptProperty, DefaultValue(30f)]
	public float Range
	{
		get => _range;
		set
		{
			_range = value;
			float v = value / RangeConversion;
			GDOmniLight.OmniRange = v;
#if CREATOR
			_sphere.Radius = v;
#endif
			OnPropertyChanged();
		}
	}

#if CREATOR
	public override void CreatorSelected()
	{
		_sphere.Visible = true;
		base.CreatorSelected();
	}

	public override void CreatorDeselected()
	{
		_sphere.Visible = false;
		base.CreatorDeselected();
	}

#endif
}
