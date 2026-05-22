// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UICorner : Instance
{
	private UIScale _topLeftRadius;
	private UIScale _topRightRadius;
	private UIScale _bottomLeftRadius;
	private UIScale _bottomRightRadius;

	[Editable, ScriptProperty]
	public UIScale CornerRadius
	{
		get => _topLeftRadius;
		set
		{
			_topLeftRadius = value;
			_topRightRadius = value;
			_bottomLeftRadius = value;
			_bottomRightRadius = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public UIScale TopLeftRadius
	{
		get => _topLeftRadius;
		set
		{
			_topLeftRadius = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public UIScale TopRightRadius
	{
		get => _topRightRadius;
		set
		{
			_topRightRadius = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public UIScale BottomLeftRadius
	{
		get => _bottomLeftRadius;
		set
		{
			_bottomLeftRadius = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public UIScale BottomRightRadius
	{
		get => _bottomRightRadius;
		set
		{
			_bottomRightRadius = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	public override void EnterTree()
	{
		if (Parent is UIField field)
		{
			field.TransformChanged.Connect(OnParentTransformChanged);
			if (!IsHidden)
				field.OnCornerControllerEnter();
		}

		ApplyToParent();
		base.EnterTree();
	}

	public override void ExitTree()
	{
		if (Parent is UIField field)
		{
			field.TransformChanged.Disconnect(OnParentTransformChanged);
			if (!IsHidden)
				field.OnCornerControllerExit();
		}

		base.ExitTree();
	}

	public override void PreDelete()
	{
		if (Parent is UIField field)
		{
			field.TransformChanged.Disconnect(OnParentTransformChanged);
			if (!IsHidden)
				field.OnCornerControllerExit();
		}

		base.PreDelete();
	}

	public override void HiddenChanged(bool to)
	{
		if (Parent is UIField field)
		{
			if (to)
				field.OnCornerControllerExit();
			else
				field.OnCornerControllerEnter();
		}

		ApplyToParent();
		base.HiddenChanged(to);
	}

	private void ApplyToParent()
	{
		if (IsHidden || Parent is not UIField field)
			return;

		float parentSize = Mathf.Min(field.AbsoluteSize.X, field.AbsoluteSize.Y);
		field.InternalSetAllCorners(
			Mathf.Max(0, _topLeftRadius.Compute(parentSize)),
			Mathf.Max(0, _topRightRadius.Compute(parentSize)),
			Mathf.Max(0, _bottomLeftRadius.Compute(parentSize)),
			Mathf.Max(0, _bottomRightRadius.Compute(parentSize))
		);
	}

	private void OnParentTransformChanged()
	{
		ApplyToParent();
	}
}
