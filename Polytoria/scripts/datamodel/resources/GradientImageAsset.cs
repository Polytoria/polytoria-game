// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class GradientImageAsset : ImageAsset
{
	private const int MaxTextureSize = 1024 * 2;
	private const int GradientSizeLimit = 24;

	private readonly GradientTexture2D _texture = new();
	private ColorSeries _series;
	private int _width;
	private int _height;
	private GradientImageFillEnum _fill;
	private Vector2 _fillFrom;
	private Vector2 _fillTo;

	[Editable, ScriptProperty]
	public ColorSeries Series
	{
		get => _series;
		set
		{
			_series = value;
			var gradient = _series.ToGradient();
			if (gradient.Colors.Length <= GradientSizeLimit)
			{
				_texture.Gradient = gradient;
			}
			else
			{
				PT.PrintWarn($"Gradient Image exceeded {GradientSizeLimit} points limit, the gradient won't be rendered.");
			}

			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public int Width
	{
		get => _width;
		set
		{
			_width = Mathf.Clamp(value, 1, MaxTextureSize);
			_texture.Width = _width;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public int Height
	{
		get => _height;
		set
		{
			_height = Mathf.Clamp(value, 1, MaxTextureSize);
			_texture.Height = _height;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public GradientImageFillEnum Fill
	{
		get => _fill;
		set
		{
			_fill = value;
			_texture.Fill = value switch
			{
				GradientImageFillEnum.Linear => GradientTexture2D.FillEnum.Linear,
				GradientImageFillEnum.Radial => GradientTexture2D.FillEnum.Radial,
				GradientImageFillEnum.Square => GradientTexture2D.FillEnum.Square,
				_ => GradientTexture2D.FillEnum.Linear,
			};
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 FillFrom
	{
		get => _fillFrom;
		set
		{
			_fillFrom = value;
			_texture.FillFrom = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 FillTo
	{
		get => _fillTo;
		set
		{
			_fillTo = value;
			_texture.FillTo = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void InitOverrides()
	{
		Series = ColorSeries.New(new(1, 1, 1), new(0, 0, 0));
		Fill = GradientImageFillEnum.Linear;
		FillFrom = new(0, 0);
		FillTo = new(1, 0);
		Width = 64;
		Height = 64;

		base.InitOverrides();
	}

	public static void RegisterAsset()
	{
		RegisterType<GradientImageAsset>();
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(_texture);
	}

	[ScriptEnum]
	public enum GradientImageFillEnum
	{
		Linear,
		Radial,
		Square
	}
}
