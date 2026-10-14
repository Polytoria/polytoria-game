// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;
using System;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class LocalImageAsset : ImageAsset
{
	private string _imagePath;
	private Image _image;
	private Texture2D _texture;

	[Editable, ScriptProperty]
	public string ImagePath
	{
		get => _imagePath;
		set
		{
			_imagePath = value;

			_image = Image.LoadFromFile(ImagePath);
			if (_image != null)
			{
				_texture = ImageTexture.CreateFromImage(_image);
			}

			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(_texture);
	}

}
