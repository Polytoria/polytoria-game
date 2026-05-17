// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Mobile.UI;

public partial class FeedPostCard : Node
{
	[Export] private Label _usernameLabel = null!;
	[Export] private Label _postDateLabel = null!;
	[Export] private Label _locationLabel = null!;
	[Export] private Label _contentLabel = null!;
	[Export] private TextureRect _pfpRect = null!;
	[Export] private TextureRect _mediaRect = null!;
	[Export] private Label _likeLabel = null!;
	[Export] private Label _commentLabel = null!;

	private readonly PTImageAsset _pfpAsset = new();

	public APIFeedPostData Data;

	public override void _Ready()
	{
		_pfpAsset.ResourceLoaded += OnPFPLoaded;
		_pfpAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_pfpAsset.ImageID = (uint)Data.Author.Id;
		_pfpAsset.LoadResource();
		_usernameLabel.Text = Data.Author.Username;
		_postDateLabel.Text = Data.PostedAt.ToShortDateString();
		_contentLabel.Text = Data.Content;
		_likeLabel.Text = Data.LikeCount.ToString();
		_commentLabel.Text = Data.Comments.Length.ToString();

		if (Data.PlaceID != null)
		{
			_locationLabel.Visible = true;
			_locationLabel.Text = Data.PlaceName;
		}
		else
		{
			_locationLabel.Visible = false;
		}

		if (Data.MediaUrl != null)
		{
			_mediaRect.Visible = true;
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = Data.MediaUrl }, (resource) =>
			{
				_mediaRect.Texture = (Texture2D)resource;
			});
		}
		else
		{
			_mediaRect.Visible = false;
		}
	}

	private void OnPFPLoaded(Resource resource)
	{
		_pfpRect.Texture = (Texture2D)resource;
	}
}
