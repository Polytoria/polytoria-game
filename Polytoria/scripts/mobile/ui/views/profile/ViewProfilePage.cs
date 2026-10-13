// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewProfilePage : MobileViewBase
{
	[Export] private AvatarPreview _avatar = null!;
	[Export] private Label _usernameLabel = null!;
	[Export] private Label _memberSinceLabel = null!;
	[Export] private Label _descriptionLabel = null!;
	[Export] private Control _aboutCard = null!;

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

	public override void ShowView(object? args)
	{
		_avatar.Wave();
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

		_avatar.LoadUser(userID);

		try
		{
			APIUserInfo user = await PolyAPI.GetUserFromID(userID);

			_usernameLabel.Text = user.Username;
			_memberSinceLabel.Text = "Member since " + user.RegisteredAt.ToString("MMM d, yyyy");
			_descriptionLabel.Text = user.Description;
			_aboutCard.Visible = !string.IsNullOrWhiteSpace(user.Description);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}
}
