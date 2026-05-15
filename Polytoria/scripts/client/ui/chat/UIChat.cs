// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Chat;

public partial class UIChat : Control
{
	private const int MaxMessages = 100;
	private const string ChatLabelPath = "res://scenes/client/ui/chat/chat_label.tscn";
	[Export] private LineEdit _chatField = null!;
	[Export] private Control _chatLayout = null!;
	[Export] private ScrollContainer _chatScroll = null!;
	[Export] private AnimationPlayer _animPlayer = null!;
	[Export] private Button _sendButton = null!;
	[Export] private AnimationPlayer _sendAnim = null!;
	[Export] private Control _chatFieldPanel = null!;

	public CoreUIRoot CoreUI = null!;
	private World Root => CoreUI.Root;
	private Player LocalPlayer => Root.Players.LocalPlayer;

	public bool IsOn = false;

	private readonly Queue<UIChatLabel> _pendingMessages = [];
	private readonly List<UIChatLabel> _chatMessages = [];

	public override void _Ready()
	{
		_chatField.TextSubmitted += OnTextSubmitted;
		_chatField.GuiInput += OnGuiInput;
		Root.Chat.NewChatMessage.Connect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Connect(OnMessageDeclined);
		Root.Chat.MessageReceived.Connect(OnMessageReceived);
		_sendButton.Pressed += OnSendButtonPressed;

		if (!LocalPlayer.CanChat || LocalPlayer.IsAgeRestricted)
		{
			if (!LocalPlayer.CanChat)
			{
				_chatField.Text = "Please verify your email to send chats";
			}
			else if (LocalPlayer.IsAgeRestricted)
			{
				// Disable chat field entirely on age restricted accounts
				_chatFieldPanel.Visible = false;
			}
			_chatField.Editable = false;
			_sendButton.Visible = false;
		}
	}

	public override void _ExitTree()
	{
		Root.Chat.NewChatMessage.Disconnect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Disconnect(OnMessageDeclined);
		Root.Chat.MessageReceived.Disconnect(OnMessageReceived);
		_chatField.TextSubmitted -= OnTextSubmitted;
		_chatField.GuiInput -= OnGuiInput;
		_sendButton.Pressed -= OnSendButtonPressed;
		base._ExitTree();
	}

	private void OnGuiInput(InputEvent @event)
	{
		// Handle ESC key
		if (@event is InputEventKey k && k.Keycode == Key.Escape)
		{
			GetViewport().GuiReleaseFocus();
		}
	}

	private void OnMessageReceived(string msg)
	{
		CreateNewChatLabel("", msg);
	}

	private void OnSendButtonPressed()
	{
		SendMessage(_chatField.Text);
		_sendAnim.Play("send");
	}

	private void OnTextSubmitted(string newText)
	{
		SendMessage(newText);
	}

	private void SendMessage(string text)
	{
		if (!Visible) return;

		// Release focus from chat field now
		_chatField.ReleaseFocus();

		if (string.IsNullOrWhiteSpace(text))
		{
			// null/whitespace, return
			return;
		}
		_chatField.Text = "";

		// Handle commands
		if (text.StartsWith('/'))
		{
			string[] cmd = text.Split(' ');

			if (cmd[0] == "/spectator")
			{
				Root.Capture.OpenSpectatorView();
				return;
			}
			else if (cmd[0] == "/kick")
			{
				Root.Players.AdminKick(cmd[1]);
				return;
			}

			string emoteName = cmd[0][1..];
			Root.Players.LocalPlayer.PlayEmote(emoteName);
			return;
		}

		UIChatLabel newPending = NewChatMessage(Root.Players.LocalPlayer, text);
		_pendingMessages.Enqueue(newPending);
		newPending.IsPending = true;

		Root.Chat.SendChatMessage(text);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("chat"))
		{
			_chatField.GrabFocus();
		}
		base._UnhandledInput(@event);
	}

	public void SetEnabled(bool enabled)
	{
		if (enabled && !IsOn)
		{
			_animPlayer.Play("open");
		}
		else if (IsOn)
		{
			_animPlayer.Play("close");
		}
		IsOn = enabled;
	}

	private void OnNewChatMessage(Player from, string msg)
	{
		if (from == World.Current!.Players.LocalPlayer)
		{
			UIChatLabel label = _pendingMessages.Dequeue();
			label.IsPending = false;
			label.Content = msg;
			return;
		}
		NewChatMessage(from, msg);
	}

	private void OnMessageDeclined()
	{
		UIChatLabel label = _pendingMessages.Dequeue();
		label.IsDeclined = true;
	}

	private UIChatLabel NewChatMessage(Player from, string msg)
	{
		return CreateNewChatLabel(from.Name, msg, from.ChatColor);
	}

	public UIChatLabel CreateNewChatLabel(string authorName, string content, Color? chatColor = null)
	{
		UIChatLabel chatLabel = Globals.CreateInstanceFromScene<UIChatLabel>(ChatLabelPath);
		chatLabel.AuthorName = authorName;
		chatLabel.Content = content;
		if (chatColor.HasValue)
		{
			chatLabel.NameColor = chatColor.Value;
		}
		_chatLayout.AddChild(chatLabel);
		_chatMessages.Add(chatLabel);

		// Scroll to the bottom only if the chat is at the bottom
		VScrollBar vScrollBar = _chatScroll.GetVScrollBar();
		bool atBottom = vScrollBar.Value + 5 >= (vScrollBar.MaxValue - vScrollBar.Page);
		if (atBottom)
		{
			PT.CallDeferred(() =>
			{
				int scrollVal = (int)vScrollBar.MaxValue + 1000;
				_chatScroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollVal);
			});
		}

		// Clean up old chat logs
		if (_chatMessages.Count > MaxMessages)
		{
			var oldest = _chatMessages[0];
			_chatLayout.RemoveChild(oldest);
			oldest.QueueFree();
			_chatMessages.RemoveAt(0);
		}

		return chatLabel;
	}
}
