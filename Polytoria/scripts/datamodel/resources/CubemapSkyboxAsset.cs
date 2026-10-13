// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Enums;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public sealed partial class CubemapSkyboxAsset : SkyboxAsset
{
	private ImageAsset? _topImage, _bottomImage, _leftImage, _rightImage, _frontImage, _backImage;
	private TextureFilterEnum _textureFilter = TextureFilterEnum.Linear;

	[Editable, ScriptProperty]
	public ImageAsset? TopImage
	{
		get => _topImage;
		set => SetFace(ref _topImage, value);
	}

	[Editable, ScriptProperty]
	public ImageAsset? BottomImage
	{
		get => _bottomImage;
		set => SetFace(ref _bottomImage, value);
	}

	[Editable, ScriptProperty]
	public ImageAsset? LeftImage
	{
		get => _leftImage;
		set => SetFace(ref _leftImage, value);
	}

	[Editable, ScriptProperty]
	public ImageAsset? RightImage
	{
		get => _rightImage;
		set => SetFace(ref _rightImage, value);
	}

	[Editable, ScriptProperty]
	public ImageAsset? FrontImage
	{
		get => _frontImage;
		set => SetFace(ref _frontImage, value);
	}

	[Editable, ScriptProperty]
	public ImageAsset? BackImage
	{
		get => _backImage;
		set => SetFace(ref _backImage, value);
	}

	[Editable, ScriptProperty]
	public TextureFilterEnum TextureFilter
	{
		get => _textureFilter;
		set
		{
			_textureFilter = value;
			NotifyChanged();
		}
	}

	private void SetFace(ref ImageAsset? field, ImageAsset? value)
	{
		if (field != null)
		{
			field.ResourceLoaded -= OnFaceResourceLoaded;
			field.UnlinkFrom(this);
		}
		field = value;
		if (field != null)
		{
			field.LinkTo(this);
			field.ResourceLoaded += OnFaceResourceLoaded;
			if (!field.IsResourceLoaded) field.QueueLoadResource();
		}
		NotifyChanged();
	}

	private void OnFaceResourceLoaded(Resource? _) => NotifyChanged();

	internal override void Apply(ShaderMaterial mat)
	{
		mat.SetShaderParameter("use_nearest_filter", _textureFilter is TextureFilterEnum.Nearest or TextureFilterEnum.NearestNoMipmaps);
		ApplyFace(mat, "top", _topImage);
		ApplyFace(mat, "bottom", _bottomImage);
		ApplyFace(mat, "left", _leftImage);
		ApplyFace(mat, "right", _rightImage);
		ApplyFace(mat, "front", _frontImage);
		ApplyFace(mat, "back", _backImage);
	}

	private static void ApplyFace(ShaderMaterial mat, string uniform, ImageAsset? asset)
	{
		bool ready = asset != null && asset.IsResourceLoaded && asset.Resource is Texture2D;
		mat.SetShaderParameter($"{uniform}_has_image", ready);
		if (ready)
		{
			mat.SetShaderParameter($"{uniform}_linear", (Texture2D)asset!.Resource!);
			mat.SetShaderParameter($"{uniform}_nearest", (Texture2D)asset.Resource!);
		}
	}
}
