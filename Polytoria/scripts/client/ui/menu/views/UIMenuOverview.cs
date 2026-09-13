// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using Polytoria.Utils;

namespace Polytoria.Client.UI;

public sealed partial class UIMenuOverview : UIMenuViewBase
{
	[Export] private Label _placeTypeLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _placeCreatorLabel = null!;
	[Export] private TextureRect _placeThumbnailRect = null!;
	[Export] private TextureRect _statPlayerImage = null!;
	[Export] private Label _statPlayerNameLabel = null!;
	[Export] private Label _statTimePlayedLabel = null!;
	[Export] private Label _statPlayerCountLabel = null!;
	[Export] private Label _statInstanceCountLabel = null!;
	[Export] private Button _screenshotButton = null!;
	[Export] private Button _respawnButton = null!;
	[Export] private Button _leaveButton = null!;
	[Export] private Button _reportButton = null!;

	private PTImageAsset? _userAvatarImage;
	private PTImageAsset? _placeThumbnailImage;

	public override void _Ready()
	{
		_screenshotButton.Pressed += OnScreenshot;
		_respawnButton.Pressed += OnRespawn;
		_leaveButton.Pressed += OnLeave;
		_reportButton.Pressed += OnReport;
		base._Ready();
	}

	public override void _ExitTree()
	{
		_screenshotButton.Pressed -= OnScreenshot;
		_respawnButton.Pressed -= OnRespawn;
		_leaveButton.Pressed -= OnLeave;
		_reportButton.Pressed -= OnReport;
		base._ExitTree();
	}

	private void OnReport()
	{
		if (Menu.CoreUI.Root.IsLocalTest) return;
		OS.ShellOpen("https://polytoria.com/report/place/" + Menu.CoreUI.Root.WorldID);
	}

	private void OnLeave()
	{
		Menu.CoreUI.Root.Entry?.LeaveGame();
	}

	private void OnRespawn()
	{
		if (!Menu.CoreUI.Service.CanRespawn) { return; }
		Menu.CoreUI.GameMenu.HideMenu();
		Menu.CoreUI.Root.Players.LocalPlayer.Kill();
	}

	private void OnScreenshot()
	{
		Menu.CoreUI.GameMenu.HideMenu();
		Menu.CoreUI.Root.Capture.TakePhoto();
	}

	public override void ShowView()
	{
		SetProcess(true);

		World root = Menu.CoreUI.Root;
		if (root.WorldInfo.HasValue)
		{
			_placeTypeLabel.Visible = true;
			_placeCreatorLabel.Visible = true;
			_placeNameLabel.Text = root.WorldInfo.Value.Name;
			_placeTypeLabel.Text = root.WorldInfo.Value.Genre.Capitalize();
			_placeCreatorLabel.Text = "By " + root.WorldInfo.Value.Creator.Name;

			_placeThumbnailImage = new();
			_placeThumbnailImage.ResourceLoaded += OnWorldThumbnailLoaded;
			_placeThumbnailImage.ImageType = ImageTypeEnum.WorldThumbnail;
			_placeThumbnailImage.ImageID = (uint)root.FirstWorldMedia;
			_placeThumbnailImage.LoadResource();
		}
		else
		{
			_placeTypeLabel.Visible = false;
			_placeCreatorLabel.Visible = false;
			if (root.WorldID == 0)
			{
				_placeNameLabel.Text = "Local Testing";
			}
			else
			{
				_placeNameLabel.Text = "Unknown";
			}
		}

		_statPlayerNameLabel.Text = root.Players.LocalPlayer.Name;

		if (_userAvatarImage == null)
		{
			_userAvatarImage = new();
			_userAvatarImage.ResourceLoaded += OnAvatarImageLoaded;
			_userAvatarImage.ImageType = ImageTypeEnum.UserAvatar;
			_userAvatarImage.ImageID = (uint)root.Players.LocalPlayer.UserID;
			_userAvatarImage.LoadResource();
		}

		if (Menu.CoreUI.Service.CanRespawn)
		{
			_respawnButton.Modulate = new(1, 1, 1, 1f);
			_respawnButton.MouseFilter = MouseFilterEnum.Stop;
			_respawnButton.MouseDefaultCursorShape = CursorShape.PointingHand;
		}
		else
		{
			_respawnButton.Modulate = new(1, 1, 1, 0.5f);
			_respawnButton.MouseFilter = MouseFilterEnum.Ignore;
			_respawnButton.MouseDefaultCursorShape = CursorShape.Forbidden;
		}

		base.ShowView();
	}

	private void OnWorldThumbnailLoaded(Resource resource)
	{
		_placeThumbnailRect.Texture = (Texture2D)resource;
	}

	private void OnAvatarImageLoaded(Resource resource)
	{
		_statPlayerImage.Texture = (Texture2D)resource;
	}

	public override void _Process(double delta)
	{
		World root = Menu.CoreUI.Root;
		_statInstanceCountLabel.Text = root.InstanceCount.ToString() + " Instances";
		_statTimePlayedLabel.Text = "Playing for " + TimeUtils.FormatSeconds((long)root.UpTime);
		_statPlayerCountLabel.Text = root.Players.PlayersCount.ToString() + " players in the server";
		base._Process(delta);
	}

	public override void HideView()
	{
		SetProcess(false);
		base.HideView();
	}
}
