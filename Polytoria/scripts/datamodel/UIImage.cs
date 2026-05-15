// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIImage : UIField
{
	public TextureRect GDTextureRect = null!;

	private ImageAsset? _imageAsset;
	private string _imageID = "";
	private ImageTypeEnum _imageType;
	private TextureFilterEnum _textureFilter;
	private Color _color = new(1, 1, 1, 1);
	private ImageStretchModeEnum _stretchMode = ImageStretchModeEnum.Stretch;
	private bool _flipH = false;
	private bool _flipV = false;

	[Editable, ScriptProperty, Export]
	public ImageAsset? Image
	{
		get => _imageAsset;
		set
		{
			if (_imageAsset != null && _imageAsset != value)
			{
				_imageAsset.ResourceLoaded -= OnResourceLoaded;
				_imageAsset.UnlinkFrom(this);
			}
			_imageAsset = value;

			SetToDefaultImage();

			if (_imageAsset != null)
			{
				Loading = true;
				_imageAsset.LinkTo(this);
				_imageAsset.ResourceLoaded += OnResourceLoaded;

				if (_imageAsset.IsResourceLoaded && _imageAsset.Resource != null)
				{
					OnResourceLoaded(_imageAsset.Resource);
				}
				else
				{
					_imageAsset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Image instead"), CloneIgnore]
	public string ImageID
	{
		get => _imageID;
		set
		{
			_imageID = value;
			CreatePTImageAsset();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Image instead"), CloneIgnore]
	public ImageTypeEnum ImageType
	{
		get => _imageType;
		set
		{
			_imageType = value;
			CreatePTImageAsset();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			NodeControl.SelfModulate = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageStretchModeEnum StretchMode
	{
		get => _stretchMode;
		set
		{
			_stretchMode = value;
			GDTextureRect.StretchMode = value switch
			{
				ImageStretchModeEnum.Stretch => TextureRect.StretchModeEnum.Scale,
				ImageStretchModeEnum.Centered => TextureRect.StretchModeEnum.KeepAspectCentered,
				ImageStretchModeEnum.Covered => TextureRect.StretchModeEnum.KeepAspectCovered,
				_ => TextureRect.StretchModeEnum.Scale
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(TextureFilterEnum.Linear)]
	public TextureFilterEnum TextureFilter
	{
		get => _textureFilter;
		set
		{
			_textureFilter = value;
			GDTextureRect.TextureFilter = value switch
			{
				TextureFilterEnum.Nearest => CanvasItem.TextureFilterEnum.Nearest,
				TextureFilterEnum.NearestNoMipmaps => CanvasItem.TextureFilterEnum.Nearest,
				TextureFilterEnum.Linear => CanvasItem.TextureFilterEnum.Linear,
				TextureFilterEnum.LinearNoMipmaps => CanvasItem.TextureFilterEnum.Linear,
				_ => throw new IndexOutOfRangeException("Texture filter mode out of range"),
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool FlipHorizontal
	{
		get => _flipH;
		set
		{
			_flipH = value;
			GDTextureRect.FlipH = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool FlipVertical
	{
		get => _flipV;
		set
		{
			_flipV = value;
			GDTextureRect.FlipV = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public bool Loading { get; private set; } = false;

	private void CreatePTImageAsset()
	{
		if (!uint.TryParse(_imageID, out uint result))
		{
			SetToDefaultImage();
			return;
		}
		PTImageAsset polyImg = New<PTImageAsset>();
		Image = polyImg;
		polyImg.ImageType = _imageType;
		polyImg.ImageID = result;
	}

	public override Node CreateGDNode()
	{
		return new TextureRect();
	}

	public override void InitGDNode()
	{
		GDTextureRect = (TextureRect)GDNode;
		GDTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		base.InitGDNode();
	}

	public override void Init()
	{
		base.Init();
		IgnoreMouse = true;
		StretchMode = ImageStretchModeEnum.Stretch;
	}

	private void SetToDefaultImage()
	{
		GDTextureRect.Texture = GD.Load<Texture2D>("res://assets/textures/client/ui/DefaultImage.png");
	}

	private void OnResourceLoaded(Resource tex)
	{
		GDTextureRect.Texture = (Texture2D)tex;
		Loading = false;
	}

	[ScriptEnum]
	public enum ImageStretchModeEnum
	{
		Stretch,
		Centered,
		Covered,
	}
}
