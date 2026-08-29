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
	private Vector2 _textureScale = Vector2.One;
	private Vector2 _textureOffset = Vector2.Zero;
	private Color _color = new(1, 1, 1, 1);
	private ImageStretchModeEnum _stretchMode = ImageStretchModeEnum.Stretch;
	private bool _flipH = false;
	private bool _flipV = false;
	private Texture2D? _loadedTexture;
	private ShaderMaterial? _shaderMaterial;

	private const string UV_SHADER = """
    shader_type canvas_item;
    uniform vec2 texture_scale = vec2(1.0, 1.0);
    uniform vec2 texture_offset = vec2(0.0, 0.0);
    uniform vec4 modulate_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
    uniform vec2 rect_size = vec2(0.0, 0.0);
    uniform vec4 corner_radius = vec4(0.0, 0.0, 0.0, 0.0);

    void fragment() {
        vec2 uv = (UV * texture_scale) + texture_offset;
        vec4 col = texture(TEXTURE, uv) * modulate_color;

        if ((corner_radius.x + corner_radius.y + corner_radius.z + corner_radius.w) > 0.0 && rect_size.x > 0.0 && rect_size.y > 0.0) {
            vec2 p = UV * rect_size;
            float r = (UV.x < 0.5)
                ? ((UV.y < 0.5) ? corner_radius.x : corner_radius.w)
                : ((UV.y < 0.5) ? corner_radius.y : corner_radius.z);
            r = min(r, min(rect_size.x, rect_size.y) * 0.5);
            if (r > 0.0) {
                vec2 c = vec2((UV.x < 0.5) ? r : rect_size.x - r, (UV.y < 0.5) ? r : rect_size.y - r);
                bool inX = (UV.x < 0.5) ? (p.x < r) : (p.x > rect_size.x - r);
                bool inY = (UV.y < 0.5) ? (p.y < r) : (p.y > rect_size.y - r);
                if (inX && inY) {
                    col.a *= 1.0 - smoothstep(r - 1.0, r + 1.0, length(p - c));
                }
            }
        }
        COLOR = col;
    }
    """;

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
	public Vector2 TextureScale
	{
		get => _textureScale;
		set
		{
			_textureScale = value;
			ApplyTexture();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 TextureOffset
	{
		get => _textureOffset;
		set
		{
			_textureOffset = value;
			ApplyTexture();
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
			ApplyTexture();
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
		GDTextureRect.Resized += OnControlResized;
	}

	private void ApplyTexture()
	{
		if (GDTextureRect == null || _loadedTexture == null) return;

		(float tl, float tr, float bl, float br) = InternalGetCorners();
		bool hasCorners = tl > 0 || tr > 0 || bl > 0 || br > 0;

		if (_textureScale == Vector2.One && _textureOffset == Vector2.Zero && !hasCorners)
		{
			GDTextureRect.Material = null;
			GDTextureRect.Texture = _loadedTexture;
			GDTextureRect.SelfModulate = _color;
			return;
		}

		if (_shaderMaterial == null)
		{
			Shader shader = new();
			shader.Code = UV_SHADER;
			_shaderMaterial = new ShaderMaterial();
			_shaderMaterial.Shader = shader;
		}

		_shaderMaterial.SetShaderParameter("texture_scale", _textureScale);
		_shaderMaterial.SetShaderParameter("texture_offset", _textureOffset);
		_shaderMaterial.SetShaderParameter("modulate_color", _color);
		_shaderMaterial.SetShaderParameter("corner_radius", new Vector4(tl, tr, br, bl));
		_shaderMaterial.SetShaderParameter("rect_size", GDTextureRect.Size);
		GDTextureRect.SelfModulate = new Color(1, 1, 1, 1);
		GDTextureRect.Texture = _loadedTexture;
		GDTextureRect.TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;
		GDTextureRect.Material = _shaderMaterial;
	}

	protected override void OnCornerRadiusChanged()
	{
		ApplyTexture();
	}

	private void OnControlResized()
	{
		if (_shaderMaterial != null && GDTextureRect?.Material == _shaderMaterial)
		{
			_shaderMaterial.SetShaderParameter("rect_size", GDTextureRect.Size);
		}
	}

	private void SetToDefaultImage()
	{
		_loadedTexture = GD.Load<Texture2D>("res://assets/textures/client/ui/DefaultImage.png");
		ApplyTexture();
	}

	private void OnResourceLoaded(Resource tex)
	{
		_loadedTexture = (Texture2D)tex;
		ApplyTexture();
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
