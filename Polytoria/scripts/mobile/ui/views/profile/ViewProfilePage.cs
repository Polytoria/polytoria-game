// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewProfilePage : MobileViewBase
{
	private TextureRect _avatarRect = null!;
	private Label _usernameLabel = null!;
	private Label _descriptionLabel = null!;
	private Label _memberSinceLabel = null!;

	private readonly PTImageAsset _avatarAsset = new();

	public override void _Ready()
	{
		_avatarRect = GetNode<TextureRect>("ScrollContainer/VBoxContainer/AvatarCenter/Avatar");
		_usernameLabel = GetNode<Label>("ScrollContainer/VBoxContainer/Username");
		_descriptionLabel = GetNode<Label>("ScrollContainer/VBoxContainer/DescriptionMargin/Description");
		_memberSinceLabel = GetNode<Label>("ScrollContainer/VBoxContainer/MemberSince");

		_avatarAsset.ResourceLoaded += OnAvatarLoaded;
		base._Ready();
	}

	public override void _EnterTree()
	{
		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		PolyMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		base._ExitTree();
	}

	private void OnUserAuthenticated(APIMeResponse response)
	{
		LoadProfile();
	}

	private void OnAvatarLoaded(Resource resource)
	{
		_avatarRect.Texture = (Texture2D)resource;
	}

	public override void ShowView(object? args)
	{
		LoadProfile();
		base.ShowView(args);
	}

	private async void LoadProfile()
	{
		int userID = PolyMobileAuthAPI.CurrentUserInfo.Id;
		if (userID == 0)
		{
			return;
		}

		try
		{
			APIUserInfo user = await PolyAPI.GetUserFromID(userID);

			_usernameLabel.Text = user.Username;
			_descriptionLabel.Text = user.Description;
			_memberSinceLabel.Text = "Member since " + user.RegisteredAt.ToString("MMM d, yyyy");

			_avatarAsset.ImageType = ImageTypeEnum.UserAvatar;
			_avatarAsset.ImageID = (uint)user.Id;
			_avatarAsset.LoadResource();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}
}
