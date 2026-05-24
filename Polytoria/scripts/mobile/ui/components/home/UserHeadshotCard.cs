// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class UserHeadshotCard : Node
{
	[Export] public uint UserID;

	[Export] private TextureRect _imageRect = null!;
	[Export] private Label _usernameLabel = null!;

	private readonly PTImageAsset _iconAsset = new();

	public override void _Ready()
	{
		_imageRect.Texture = null;
		_usernameLabel.Text = "";
		_iconAsset.ResourceLoaded += OnIconLoaded;
		LoadUserCard();
	}

	private void OnIconLoaded(Resource resource)
	{
		_imageRect.Texture = (Texture2D)resource;
	}

	public async void LoadUserCard()
	{
		_iconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_iconAsset.ImageID = UserID;
		_iconAsset.LoadResource();

		try
		{
			APIUserInfo userData = await PolyAPI.GetUserFromID((int)UserID);

			_usernameLabel.Text = userData.Username;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}
}
