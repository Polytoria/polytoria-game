// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIStroke : Instance
{
	private UIScale _thickness;
	private Color _color = new(0, 0, 0);
	private Panel? _strokePanel;
	private StyleBoxFlat? _strokeStyleBox;

	[Editable, ScriptProperty]
	public UIScale Thickness
	{
		get => _thickness;
		set
		{
			_thickness = value;
			ApplyToParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
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
				field.OnStrokeControllerEnter();
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
				field.OnStrokeControllerExit();
		}

		CleanupStrokePanel();
		base.ExitTree();
	}

	public override void PreDelete()
	{
		if (Parent is UIField field)
		{
			field.TransformChanged.Disconnect(OnParentTransformChanged);
			if (!IsHidden)
				field.OnStrokeControllerExit();
		}

		CleanupStrokePanel();
		base.PreDelete();
	}

	public override void HiddenChanged(bool to)
	{
		if (Parent is UIField field)
		{
			if (to)
			{
				field.OnStrokeControllerExit();
				CleanupStrokePanel();
			}
			else
			{
				field.OnStrokeControllerEnter();
				ApplyToParent();
			}
		}

		base.HiddenChanged(to);
	}

	private void ApplyToParent()
	{
		if (IsHidden || Parent is not UIField field || field.NodeControl == null)
			return;

		float parentSize = Mathf.Min(field.AbsoluteSize.X, field.AbsoluteSize.Y);
		int width = Mathf.RoundToInt(Mathf.Max(0, _thickness.Compute(parentSize)));
		field.InternalSetStroke(width, _color);

		if (_strokeStyleBox != null)
		{
			var corners = field.InternalGetCorners();
			_strokeStyleBox.CornerRadiusTopLeft = Mathf.RoundToInt(corners.TopLeft);
			_strokeStyleBox.CornerRadiusTopRight = Mathf.RoundToInt(corners.TopRight);
			_strokeStyleBox.CornerRadiusBottomLeft = Mathf.RoundToInt(corners.BottomLeft);
			_strokeStyleBox.CornerRadiusBottomRight = Mathf.RoundToInt(corners.BottomRight);
		}

		if (field.NodeControl is not Panel)
			EnsureStrokePanel(field);
	}

	private void EnsureStrokePanel(UIField field)
	{
		if (_strokePanel != null) return;

		var corners = field.InternalGetCorners();
		StyleBoxFlat sb = new()
		{
			AntiAliasing = true,
			AntiAliasingSize = 2,
			CornerRadiusTopLeft = Mathf.RoundToInt(corners.TopLeft),
			CornerRadiusTopRight = Mathf.RoundToInt(corners.TopRight),
			CornerRadiusBottomLeft = Mathf.RoundToInt(corners.BottomLeft),
			CornerRadiusBottomRight = Mathf.RoundToInt(corners.BottomRight),
		};

		_strokeStyleBox = sb;
		_strokePanel = UIField.CreateOverlayPanel();
		_strokePanel.AddThemeStyleboxOverride("panel", sb);

		field.NodeControl.AddChild(_strokePanel);
		field.NodeControl.MoveChild(_strokePanel, 0);
	}

	private void CleanupStrokePanel()
	{
		_strokePanel?.QueueFree();
		_strokePanel = null;
		_strokeStyleBox = null;
	}

	private void OnParentTransformChanged()
	{
		ApplyToParent();
	}
}
