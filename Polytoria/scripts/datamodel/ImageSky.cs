// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using System;
using Obsolete = Polytoria.Attributes.ObsoleteAttribute;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ImageSky : Sky
{
	private readonly Texture2D _empty = GD.Load<Texture2D>("res://assets/textures/empty.png");
	private static readonly Shader _linearShader = GD.Load<Shader>("res://resources/shaders/imagesky_linear.gdshader");
	private static readonly Shader _nearestShader = GD.Load<Shader>("res://resources/shaders/imagesky_nearest.gdshader");

	private int _topId = 14168;
	private int _bottomId = 14166;
	private int _leftId = 14154;
	private int _rightId = 14155;
	private int _frontId = 14153;
	private int _backId = 14151;
	private ImageAsset? _topImage;
	private ImageAsset? _bottomImage;
	private ImageAsset? _leftImage;
	private ImageAsset? _rightImage;
	private ImageAsset? _frontImage;
	private ImageAsset? _backImage;
	private TextureFilterEnum _textureFilter = TextureFilterEnum.Linear;

	[Editable, ScriptProperty]
	public ImageAsset? TopImage
	{
		get => _topImage;
		set
		{
			if (_topImage != null && _topImage != value)
			{
				_topImage.ResourceLoaded -= OnTopImageLoaded;
				_topImage.UnlinkFrom(this);
			}
			_topImage = value;
			OnTopImageLoaded(null);
			if (_topImage != null)
			{
				_topImage.LinkTo(this);
				_topImage.ResourceLoaded += OnTopImageLoaded;
				if (_topImage.IsResourceLoaded && _topImage.Resource != null)
				{
					OnTopImageLoaded(_topImage.Resource);
				}
				else
				{
					_topImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? BottomImage
	{
		get => _bottomImage;
		set
		{
			if (_bottomImage != null && _bottomImage != value)
			{
				_bottomImage.ResourceLoaded -= OnBottomImageLoaded;
				_bottomImage.UnlinkFrom(this);
			}
			_bottomImage = value;
			OnBottomImageLoaded(null);
			if (_bottomImage != null)
			{
				_bottomImage.LinkTo(this);
				_bottomImage.ResourceLoaded += OnBottomImageLoaded;
				if (_bottomImage.IsResourceLoaded && _bottomImage.Resource != null)
				{
					OnBottomImageLoaded(_bottomImage.Resource);
				}
				else
				{
					_bottomImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? LeftImage
	{
		get => _leftImage;
		set
		{
			if (_leftImage != null && _leftImage != value)
			{
				_leftImage.ResourceLoaded -= OnLeftImageLoaded;
				_leftImage.UnlinkFrom(this);
			}
			_leftImage = value;
			OnLeftImageLoaded(null);
			if (_leftImage != null)
			{
				_leftImage.LinkTo(this);
				_leftImage.ResourceLoaded += OnLeftImageLoaded;
				if (_leftImage.IsResourceLoaded && _leftImage.Resource != null)
				{
					OnLeftImageLoaded(_leftImage.Resource);
				}
				else
				{
					_leftImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? RightImage
	{
		get => _rightImage;
		set
		{
			if (_rightImage != null && _rightImage != value)
			{
				_rightImage.ResourceLoaded -= OnRightImageLoaded;
				_rightImage.UnlinkFrom(this);
			}
			_rightImage = value;
			OnRightImageLoaded(null);
			if (_rightImage != null)
			{
				_rightImage.LinkTo(this);
				_rightImage.ResourceLoaded += OnRightImageLoaded;
				if (_rightImage.IsResourceLoaded && _rightImage.Resource != null)
				{
					OnRightImageLoaded(_rightImage.Resource);
				}
				else
				{
					_rightImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? FrontImage
	{
		get => _frontImage;
		set
		{
			if (_frontImage != null && _frontImage != value)
			{
				_frontImage.ResourceLoaded -= OnFrontImageLoaded;
				_frontImage.UnlinkFrom(this);
			}
			_frontImage = value;
			OnFrontImageLoaded(null);
			if (_frontImage != null)
			{
				_frontImage.LinkTo(this);
				_frontImage.ResourceLoaded += OnFrontImageLoaded;
				if (_frontImage.IsResourceLoaded && _frontImage.Resource != null)
				{
					OnFrontImageLoaded(_frontImage.Resource);
				}
				else
				{
					_frontImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? BackImage
	{
		get => _backImage;
		set
		{
			if (_backImage != null && _backImage != value)
			{
				_backImage.ResourceLoaded -= OnBackImageLoaded;
				_backImage.UnlinkFrom(this);
			}
			_backImage = value;
			OnBackImageLoaded(null);
			if (_backImage != null)
			{
				_backImage.LinkTo(this);
				_backImage.ResourceLoaded += OnBackImageLoaded;
				if (_backImage.IsResourceLoaded && _backImage.Resource != null)
				{
					OnBackImageLoaded(_backImage.Resource);
				}
				else
				{
					_backImage.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public TextureFilterEnum TextureFilter
	{
		get => _textureFilter;
		set
		{
			_textureFilter = value;
			RebuildMaterial();
			Root.Lighting.ApplySky(this);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(TopId))]
	public int TopId
	{
		get => _topId;
		set
		{
			_topId = value;
			TopImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(BottomId))]
	public int BottomId
	{
		get => _bottomId;
		set
		{
			_bottomId = value;
			BottomImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(LeftId))]
	public int LeftId
	{
		get => _leftId;
		set
		{
			_leftId = value;
			LeftImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(RightId))]
	public int RightId
	{
		get => _rightId;
		set
		{
			_rightId = value;
			RightImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(FrontId))]
	public int FrontId
	{
		get => _frontId;
		set
		{
			_frontId = value;
			FrontImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	[Editable, Obsolete("Use Image instead"), NoSync, ScriptLegacyProperty(nameof(BackId))]
	public int BackId
	{
		get => _backId;
		set
		{
			_backId = value;
			BackImage = Root.Assets.NewPTImage((uint)value);
			OnPropertyChanged();
		}
	}

	private void OnTopImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("top", (Texture2D?)resource ?? _empty);
	}

	private void OnBottomImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("bottom", (Texture2D?)resource ?? _empty);
	}

	private void OnLeftImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("left", (Texture2D?)resource ?? _empty);
	}

	private void OnRightImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("right", (Texture2D?)resource ?? _empty);
	}

	private void OnFrontImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("front", (Texture2D?)resource ?? _empty);
	}

	private void OnBackImageLoaded(Resource? resource)
	{
		_mat.SetShaderParameter("back", (Texture2D?)resource ?? _empty);
	}

	private ShaderMaterial _mat = null!;

	private void RebuildMaterial()
	{
		var shader = _textureFilter is TextureFilterEnum.Nearest || _textureFilter is TextureFilterEnum.NearestNoMipmaps ? _nearestShader : _linearShader;
		_mat = new() { Shader = shader };
		SkyMaterial = _mat;
		ApplyTextures();
	}

	private void ApplyTextures()
	{
		_mat.SetShaderParameter("top", (Texture2D?)(_topImage?.Resource) ?? _empty);
		_mat.SetShaderParameter("bottom", (Texture2D?)(_bottomImage?.Resource) ?? _empty);
		_mat.SetShaderParameter("left", (Texture2D?)(_leftImage?.Resource) ?? _empty);
		_mat.SetShaderParameter("right", (Texture2D?)(_rightImage?.Resource) ?? _empty);
		_mat.SetShaderParameter("front", (Texture2D?)(_frontImage?.Resource) ?? _empty);
		_mat.SetShaderParameter("back", (Texture2D?)(_backImage?.Resource) ?? _empty);
	}

	public override void Init()
	{
		RebuildMaterial();
		base.Init();
	}
}
