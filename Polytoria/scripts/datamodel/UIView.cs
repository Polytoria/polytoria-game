// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Enums;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIView : UIField
{
	private Color _borderColor;
	private Color _color;
	private BorderModeEnum _borderMode;
	private float _borderWidth;
	private float _cornerRadius;


	[Editable, ScriptProperty]
	public Color BorderColor
	{
		get => _borderColor;
		set
		{
			_borderColor = value;
			if (StrokeControllerCount == 0)
				_styleBox.BorderColor = _borderColor;
			else
				SavedBorderColor = _borderColor;
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
			_styleBox.BgColor = _color;
			OnPropertyChanged();
		}
	}

	private void UpdateBorderOffset()
	{
		int rounded = Mathf.RoundToInt(_borderWidth);
		if (_borderMode == BorderModeEnum.Outline)
		{
			_styleBox.ExpandMarginBottom = rounded;
			_styleBox.ExpandMarginLeft = rounded;
			_styleBox.ExpandMarginRight = rounded;
			_styleBox.ExpandMarginTop = rounded;
		}
		else if (_borderMode == BorderModeEnum.Middle)
		{
			_styleBox.ExpandMarginBottom = rounded / 2;
			_styleBox.ExpandMarginLeft = rounded / 2;
			_styleBox.ExpandMarginRight = rounded / 2;
			_styleBox.ExpandMarginTop = rounded / 2;
		}
		else
		{
			_styleBox.ExpandMarginBottom = 0;
			_styleBox.ExpandMarginLeft = 0;
			_styleBox.ExpandMarginRight = 0;
			_styleBox.ExpandMarginTop = 0;
		}
	}

	[Editable, ScriptProperty]
	public BorderModeEnum BorderMode
	{
		get => _borderMode;
		set
		{
			_borderMode = value;
			UpdateBorderOffset();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float BorderWidth
	{
		get => _borderWidth;
		set
		{
			_borderWidth = value;
			if (_borderWidth > 0 && BorderColor.A == 0)
				_borderWidth = 0;
			int rounded = Mathf.RoundToInt(_borderWidth);
			if (StrokeControllerCount == 0)
			{
				_styleBox.BorderWidthTop = rounded;
				_styleBox.BorderWidthBottom = rounded;
				_styleBox.BorderWidthLeft = rounded;
				_styleBox.BorderWidthRight = rounded;
			}
			else
			{
				var bw = SavedBorderWidths;
				bw[0] = bw[1] = bw[2] = bw[3] = rounded;
			}
			UpdateBorderOffset();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float CornerRadius
	{
		get => _cornerRadius;
		set
		{
			_cornerRadius = value;
			int rounded = Mathf.RoundToInt(value);
			if (CornerControllerCount == 0)
			{
				_styleBox.CornerRadiusTopLeft = rounded;
				_styleBox.CornerRadiusTopRight = rounded;
				_styleBox.CornerRadiusBottomLeft = rounded;
				_styleBox.CornerRadiusBottomRight = rounded;
			}
			else
			{
				var c = SavedCorners;
				c[0] = c[1] = c[2] = c[3] = rounded;
			}
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		base.Init();
		BorderColor = new(0, 0, 0);
		Color = new(1, 1, 1);
		BorderWidth = 0;
		CornerRadius = 0;
	}

}
