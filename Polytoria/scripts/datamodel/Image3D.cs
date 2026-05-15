// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using Polytoria.Shared;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Image3D : Dynamic
{
	private ImageAsset? _asset;
	private string _imageID = "";
	private ImageTypeEnum _imageType;
	private StandardMaterial3D _material = new();
	private MeshInstance3D _mesh = null!;

	private Texture2D? _prevImg;

	private Vector2 _textureScale = Vector2.One;
	private Vector2 _textureOffset = Vector2.Zero;
	private Color _color = new(1, 1, 1);
	private bool _castShadows;
	private bool _shaded;
	private bool _faceCamera;
	private TextureFilterEnum _textureFilter;

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			_prevImg = null;
			_material.AlbedoTexture = null;
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Asset instead"), CloneIgnore, SaveIgnore]
	public string ImageID
	{
		get => _imageID;
		set
		{
			_imageID = value;
			CreatePTImageAsset();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use Asset instead"), CloneIgnore, SaveIgnore]
	public ImageTypeEnum ImageType
	{
		get => _imageType;
		set
		{
			_imageType = value;
			CreatePTImageAsset();
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty]
	public Vector2 TextureScale
	{
		get => _textureScale;
		set
		{
			_textureScale = value;
			_material.Uv1Scale = new(value.X, value.Y, 1);
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
			_material.Uv1Offset = new(value.X, value.Y, 1);
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
			_material.AlbedoColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool CastShadows
	{
		get => _castShadows;
		set
		{
			_castShadows = value;
			_mesh.CastShadow = value ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Shaded
	{
		get => _shaded;
		set
		{
			_shaded = value;
			_material.ShadingMode = value ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool FaceCamera
	{
		get => _faceCamera;
		set
		{
			_faceCamera = value;
			_material.BillboardMode = value ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled;
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
			_material.TextureFilter = value switch
			{
				TextureFilterEnum.Nearest => BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
				TextureFilterEnum.NearestNoMipmaps => BaseMaterial3D.TextureFilterEnum.Nearest,
				TextureFilterEnum.Linear => BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				TextureFilterEnum.LinearNoMipmaps => BaseMaterial3D.TextureFilterEnum.Linear,
				_ => throw new IndexOutOfRangeException("Texture filter mode out of range"),
			};
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		_material.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
		_mesh = GDNode.GetNode<MeshInstance3D>("Mesh");
		_mesh.MaterialOverride = _material;

		_material.BillboardKeepScale = true;

		Shaded = true;
		CastShadows = true;

		base.Init();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_mesh.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	private void CreatePTImageAsset()
	{
		if (!uint.TryParse(_imageID, out uint result))
		{
			return;
		}

		PTImageAsset polyImg = New<PTImageAsset>();
		Image = polyImg;
		polyImg.ImageType = _imageType;
		polyImg.ImageID = result;
	}

	private void OnResourceLoaded(Resource r)
	{
		if (r is Texture2D tex)
		{
			if (_prevImg == tex) return;
			_prevImg = tex;
			_material.AlbedoTexture = tex;

			// Set transparency depending on image's alpha
			_material.Transparency = tex.GetImage().DetectAlpha() switch
			{
				Godot.Image.AlphaMode.Blend => BaseMaterial3D.TransparencyEnum.Alpha,
				Godot.Image.AlphaMode.Bit => BaseMaterial3D.TransparencyEnum.AlphaScissor,
				_ => BaseMaterial3D.TransparencyEnum.Disabled,
			};
		}
	}
}
