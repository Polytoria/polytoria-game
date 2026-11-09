// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Chat;

public partial class UIChatButton : Button
{
	[Export] public UIChat ChatUI { get; set; } = null!;
	public CoreUIRoot CoreUI = null!;

	private Label _newBadge = null!;

	private int _unReadedCount = 0;

	public override void _Ready()
	{
		_newBadge = GetNode<Label>("NewBadge");
		Toggled += OnToggled;
		CoreUI.Root.Chat.NewChatMessage.Connect(OnNewChatMessage);
		CoreUI.Root.Chat.MessageReceived.Connect(OnMessageReceived);
	}

	private void OnMessageReceived(string _)
	{
		PutUnreadBadge();
	}

	private void OnNewChatMessage(Player plr, string _msg)
	{
		if (plr.IsMuted) return;

		PutUnreadBadge();
	}

	private void PutUnreadBadge()
	{
		if (!ButtonPressed)
		{
			_unReadedCount++;
			if (_unReadedCount > 9)
			{
				_unReadedCount = 9;
			}
			_newBadge.Text = _unReadedCount.ToString();
			_newBadge.Visible = true;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("chat"))
		{
			ButtonPressed = true;
		}
		base._UnhandledInput(@event);
	}

	private void OnToggled(bool toggleOn)
	{
		_unReadedCount = 0;
		_newBadge.Visible = false;
		ChatUI.SetEnabled(toggleOn);
	}
}
