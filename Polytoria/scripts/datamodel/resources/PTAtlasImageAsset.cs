// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting.Datatypes;
using Polytoria.Shared.AssetLoaders;
using System;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTAtlasImageAsset : PTImageAsset
{
	private Vector2 _regionPosition = Vector2.Zero;
	private Vector2 _regionSize = Vector2.Zero;
	private AtlasTexture _atlasTexture = null!;

	[Editable, ScriptProperty]
	public Vector2 RegionPosition
	{
		get => _regionPosition;
		set
		{
			_regionPosition = value;
			UpdateTextureSize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 RegionSize
	{
		get => _regionSize;
		set
		{
			_regionSize = value;
			UpdateTextureSize();
			OnPropertyChanged();
		}
	}

	internal string? DirectImageURL { get; private set; }

	public new static void RegisterAsset()
	{
		RegisterType<PTAtlasImageAsset>();
	}

	private Rect2 MakeRect()
	{
		return new Rect2
		{
			Position = _regionPosition,
			Size = _regionSize
		};
	}

	private void UpdateTextureSize()
	{
		if (_atlasTexture != null) _atlasTexture.Region = MakeRect();
	}

	public override void LoadResource()
	{
		if (ImageID == 0) { return; }
		ResourceType resourceType = ImageType switch
		{
			ImageTypeEnum.Asset => ResourceType.Decal,
			ImageTypeEnum.AssetThumbnail => ResourceType.AssetThumbnail,
			ImageTypeEnum.WorldThumbnail => ResourceType.PlaceThumbnail,
			ImageTypeEnum.UserAvatar => ResourceType.UserThumbnail,
			ImageTypeEnum.UserAvatarHeadshot => ResourceType.UserHeadshot,
			ImageTypeEnum.GuildIcon => ResourceType.GuildThumbnail,
			ImageTypeEnum.GuildBanner => ResourceType.GuildBanner,
			ImageTypeEnum.PlaceIcon => ResourceType.PlaceIcon,
			_ => throw new NotImplementedException()
		};

		AssetLoader.Singleton.GetRawCache(
			new() { Type = resourceType, ID = ImageID },
			OnResourceLoaded
		);
	}

	private void OnResourceLoaded(CacheItem cacheItem)
	{
		DirectImageURL = cacheItem.DirectURL;
		_atlasTexture = new AtlasTexture
		{
			Atlas = (Texture2D)cacheItem.Resource,
			Region = MakeRect()
		};

		InvokeResourceLoaded(_atlasTexture);
	}
}
