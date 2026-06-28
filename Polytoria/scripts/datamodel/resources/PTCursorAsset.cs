// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;
using System;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTCursorAsset : CursorAsset
{
	const int MAX_CURSOR_SIZE = 128;
	private uint _imageID;

	[Editable, ScriptProperty]
	public uint ImageID
	{
		get => _imageID;
		set
		{
			_imageID = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	internal string? DirectImageURL { get; private set; }

	public static void RegisterAsset()
	{
		RegisterType<PTCursorAsset>();
	}
	
	public override void LoadResource()
	{
		if (ImageID == 0) { return; }

		AssetLoader.Singleton.GetRawCache(
			new() { Type = ResourceType.Decal, ID = ImageID },
			OnResourceLoaded
		);
	}

	private void OnResourceLoaded(CacheItem cacheItem)
	{
		DirectImageURL = cacheItem.DirectURL;
		
		// Resize loaded image if texture is too large.
		if (cacheItem.Resource is Texture2D tex)
		{
			Image img = tex.GetImage();
			Vector2 imgSize = tex.GetSize();
			float largestBound = Math.Max(imgSize.X, imgSize.Y);
			if (largestBound > MAX_CURSOR_SIZE)
			{
				imgSize = (imgSize / largestBound) * MAX_CURSOR_SIZE;
				img.Resize((int)imgSize.X, (int)imgSize.Y);
				InvokeResourceLoaded((Resource)ImageTexture.CreateFromImage(img));
				return;
			}
		}

		InvokeResourceLoaded(cacheItem.Resource);
	}
}
