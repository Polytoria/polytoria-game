// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Abstract]
public partial class UIHVLayout : UIContainer
{
	private UILayoutAlignmentEnum _childAlignment;
	private int _spacing;

	[Editable, ScriptProperty, DefaultValue(8)]
	public int Spacing
	{
		get => _spacing;
		set
		{
			_spacing = value;
			NodeControl.AddThemeConstantOverride("separation", _spacing);
			NodeControl.AddThemeConstantOverride("h_separation", _spacing);
			NodeControl.AddThemeConstantOverride("v_separation", _spacing);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(UILayoutAlignmentEnum.Start)]
	public UILayoutAlignmentEnum ChildAlignment
	{
		get => _childAlignment;
		set
		{
			_childAlignment = value;

			if (NodeControl is BoxContainer bc)
			{
				BoxContainer.AlignmentMode alignmentMode = BoxContainer.AlignmentMode.Begin;

				switch (value)
				{
					case UILayoutAlignmentEnum.Start:
						alignmentMode = BoxContainer.AlignmentMode.Begin;
						break;
					case UILayoutAlignmentEnum.Center:
						alignmentMode = BoxContainer.AlignmentMode.Center;
						break;
					case UILayoutAlignmentEnum.End:
						alignmentMode = BoxContainer.AlignmentMode.End;
						break;
				}

				bc.Alignment = alignmentMode;
			}
			else if (NodeControl is FlowContainer fc)
			{
				FlowContainer.AlignmentMode alignmentMode = FlowContainer.AlignmentMode.Begin;

				switch (value)
				{
					case UILayoutAlignmentEnum.Start:
						alignmentMode = FlowContainer.AlignmentMode.Begin;
						break;
					case UILayoutAlignmentEnum.Center:
						alignmentMode = FlowContainer.AlignmentMode.Center;
						break;
					case UILayoutAlignmentEnum.End:
						alignmentMode = FlowContainer.AlignmentMode.End;
						break;
				}
				fc.Alignment = alignmentMode;
			}

			OnPropertyChanged();
		}
	}

	[ScriptEnum]
	public enum UILayoutAlignmentEnum
	{
		Start,
		Center,
		End,
	}
}
