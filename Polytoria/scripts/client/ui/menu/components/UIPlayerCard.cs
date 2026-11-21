// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Client.UI;

public partial class UIPlayerCard : Node
{
	[Export] private Button _reportButton = null!;
	[Export] private Button _profileButton = null!;
	[Export] private Button _muteButton = null!;

	public Player TargetPlayer = null!;

	private PTImageAsset _plrIconAsset = null!;

	public override void _Ready()
	{
		_plrIconAsset = new();
		GetNode<Label>("Layout/Label").Text = TargetPlayer.Name;

		_plrIconAsset.ResourceLoaded += OnIconLoaded;
		_plrIconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_plrIconAsset.ImageID = (uint)TargetPlayer.UserID;
		_plrIconAsset.LoadResource();

		_profileButton.Pressed += OnProfile;
		_reportButton.Pressed += OnReport;
		_muteButton.Pressed += OnMute;
		TargetPlayer.MutedChanged += UpdateMuteButton;
		_muteButton.Visible = !TargetPlayer.IsLocal;
		UpdateMuteButton();
	}

	private void OnMute()
	{
		TargetPlayer.IsMuted = !TargetPlayer.IsMuted;
	}

	private void UpdateMuteButton()
	{
		_muteButton.Text = TargetPlayer.IsMuted ? "Unmute" : "Mute";
	}

	private void OnReport()
	{
		OS.ShellOpen("https://polytoria.com/report/user/" + TargetPlayer.UserID);
	}

	private void OnProfile()
	{
		OS.ShellOpen("https://polytoria.com/users/" + TargetPlayer.UserID);
	}

	public override void _ExitTree()
	{
		_plrIconAsset.ResourceLoaded -= OnIconLoaded;
		_profileButton.Pressed -= OnProfile;
		_reportButton.Pressed -= OnReport;
		_muteButton.Pressed -= OnMute;
		TargetPlayer.MutedChanged -= UpdateMuteButton;
		base._ExitTree();
	}

	private void OnIconLoaded(Resource resource)
	{
		GetNode<TextureRect>("Layout/Icon").Texture = (Texture2D)resource;
	}
}
