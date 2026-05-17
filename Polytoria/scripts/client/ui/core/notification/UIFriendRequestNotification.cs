// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Notification;

public partial class UIFriendRequestNotification : UINotificationBase
{
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private Button _viewButton = null!;

	public override void Fire(object? data)
	{
		if (data is FriendRequestNotifyPayload)
		{
			//_iconRect.Texture = payload.Icon;
			_animPlay.Play("appear");

			//World game = NotificationCenter.CoreUI.Root;

			//_viewButton.Pressed += game.Capture.ViewCurrentPhoto;
		}
		else
		{
			QueueFree();
		}
	}

	public struct FriendRequestNotifyPayload()
	{
	}
}
