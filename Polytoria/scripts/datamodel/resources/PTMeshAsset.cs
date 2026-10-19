// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTMeshAsset : MeshAsset
{
	private uint _assetID;
	private ImageAsset? _texture;

	[Editable, ScriptProperty]
	public ImageAsset? Texture
	{
		get => _texture;
		set
		{
			if (_texture != null && _texture != value)
			{
				_texture.UnlinkFrom(this);
				_texture.ResourceLoaded -= TextureLoaded;
			}

			_texture = value;
			if (_texture != null)
			{
				_texture.LinkTo(this);
				_texture.ResourceLoaded += TextureLoaded;
				if (_texture.IsResourceLoaded && _texture.Resource != null)
				{
					TextureLoaded(_texture.Resource);
				}
				else
				{
					_texture.QueueLoadResource();
				}
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public uint AssetID
	{
		get => _assetID;
		set
		{
			_assetID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void LoadResource()
	{
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Mesh, ID = AssetID },
			InvokeResourceLoaded
		);
		Texture?.LoadResource();
	}

	private void TextureLoaded(Resource resource)
	{
		OnPropertyChanged();
	}
}
