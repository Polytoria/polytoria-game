// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Datamodel.Creator;
#endif
using Polytoria.Datamodel.Services;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class GUI : Instance
{
	private Control _control = null!;
	private bool _visible = true;
	private int _zIndex = 0;
	private bool _avoidCoreUI = false;

	[Editable, ScriptProperty]
	public bool Visible
	{
		get => _visible;
		set
		{
			_visible = value;
			_control.Visible = _visible;
			RecomputeVisible();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public int ZIndex
	{
		get => _zIndex;
		set
		{
			_zIndex = value;
			_control.ZIndex = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AvoidCoreUI
	{
		get => _avoidCoreUI;
		set
		{
			_avoidCoreUI = value;
			if (Root.SessionType != World.SessionTypeEnum.Creator)
			{
				_control.OffsetTop = value ? CoreUIService.TopInset : 0;
			}
			OnPropertyChanged();
		}
	}

	public Vector2 AbsoluteSize => _control.Size;

	public override Node CreateGDNode()
	{
		_control = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		return _control;
	}

	public override void Init()
	{
		base.Init();

		// Make fullrect
		_control.SetAnchorsPreset(Control.LayoutPreset.FullRect, true);
		_control.Resized += RecomputeChildTransforms;
	}

	public override void EnterTree()
	{
		RecomputeVisible();
		base.EnterTree();
	}

	public void RecomputeVisible()
	{
		bool isValidParent = Parent is PlayerGUI or GUI3D
#if CREATOR
		or CreatorGUI
#endif
			;

		_control.Visible = _visible && isValidParent;
	}

	public override void PreDelete()
	{
		_control.Resized -= RecomputeChildTransforms;
		base.PreDelete();
	}

	protected void RecomputeChildTransforms()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is UIField uifield)
			{
				uifield.RecomputeTransform();
			}
		}
	}
}
