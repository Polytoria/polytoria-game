// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Chat;

public partial class UIChat : Control
{
	private const int MaxMessages = 100;
	private const float AspectRatio = 400f / 240f;
	private const string ChatLabelPath = "res://scenes/client/ui/chat/chat_label.tscn";
	[Export] private LineEdit _chatField = null!;
	[Export] private Control _chatLayout = null!;
	[Export] private ScrollContainer _chatScroll = null!;
	[Export] private AnimationPlayer _animPlayer = null!;
	[Export] private Button _sendButton = null!;
	[Export] private AnimationPlayer _sendAnim = null!;
	[Export] private Control _chatFieldPanel = null!;
	[Export] private Panel _chatPanel = null!;
	[Export] private TextureRect _resizeHandle = null!;
	[Export] private UIEmojiPicker _emojiPicker = null!;
	[Export] private Button _emojiButton = null!;

	public CoreUIRoot CoreUI = null!;
	private World Root => CoreUI.Root;
	private Player LocalPlayer => Root.Players.LocalPlayer;

	public bool IsOn = false;

	private bool _isAutocompleteOpen;
	private bool _suppressAutocomplete;

	private readonly Queue<UIChatLabel> _pendingMessages = [];
	private readonly List<UIChatLabel> _chatMessages = [];

	private bool _isResizing;
	private float _resizeStartWidth;
	private float _resizeStartMaxWidth;
	private Vector2 _resizeStartMousePos;
	private readonly Vector2 _minSize = new(400, 240);

	private Tween? _resizeHandleTween;

	private const float MaxChatWidthValue = 1000f;
	private float MaxChatWidth => Mathf.Clamp(GetViewportRect().Size.X * 0.45f, _minSize.X, MaxChatWidthValue);

	public override void _Ready()
	{
		_chatField.TextSubmitted += OnTextSubmitted;
		_chatField.GuiInput += OnGuiInput;
		_chatField.TextChanged += OnChatFieldTextChanged;
		Root.Chat.NewChatMessage.Connect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Connect(OnMessageDeclined);
		Root.Chat.MessageReceived.Connect(OnMessageReceived);
		_sendButton.Pressed += OnSendButtonPressed;
		_resizeHandle.GuiInput += OnResizeHandleInput;

		_emojiPicker.Initialize();
		_emojiPicker.EmojiPicked += OnEmojiPicked;
		_emojiButton.Pressed += OnEmojiButtonPressed;
		_resizeHandle.MouseEntered += OnResizeHandleMouseEntered;
		_resizeHandle.MouseExited += OnResizeHandleMouseExited;
		GetViewport().SizeChanged += ClampToViewport;

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
			_emojiButton.Visible = false;
		}
		ClampToViewport();
	}

	public override void _ExitTree()
	{
		Root.Chat.NewChatMessage.Disconnect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Disconnect(OnMessageDeclined);
		Root.Chat.MessageReceived.Disconnect(OnMessageReceived);
		_chatField.TextSubmitted -= OnTextSubmitted;
		_chatField.GuiInput -= OnGuiInput;
		_chatField.TextChanged -= OnChatFieldTextChanged;
		_sendButton.Pressed -= OnSendButtonPressed;
		_resizeHandle.GuiInput -= OnResizeHandleInput;
		_resizeHandle.MouseEntered -= OnResizeHandleMouseEntered;
		_resizeHandle.MouseExited -= OnResizeHandleMouseExited;
		_emojiPicker.EmojiPicked -= OnEmojiPicked;
		_emojiButton.Pressed -= OnEmojiButtonPressed;
		GetViewport().SizeChanged -= ClampToViewport;
		base._ExitTree();
	}

	private void OnGuiInput(InputEvent @event)
	{
		if (@event is InputEventKey k && k.Pressed)
		{
			if (k.Keycode == Key.Escape)
			{
				if (_isAutocompleteOpen || _emojiPicker.Visible)
				{
					CloseEmojiPicker();
					GetViewport().SetInputAsHandled();
					return;
				}
				GetViewport().GuiReleaseFocus();
				GetViewport().SetInputAsHandled();
			}
			else if (k.Keycode == Key.Tab && _isAutocompleteOpen)
			{
				if (k.ShiftPressed)
					_emojiPicker.SelectPrev();
				else
					_emojiPicker.SelectNext();
				GetViewport().SetInputAsHandled();
			}
			else if (k.Keycode == Key.Enter && _isAutocompleteOpen)
			{
				string emojiName = _emojiPicker.GetSelectedEmojiName();
				if (!string.IsNullOrEmpty(emojiName))
					InsertEmojiAtCursor(emojiName);
				GetViewport().SetInputAsHandled();
			}
			else if (_isAutocompleteOpen && (k.Keycode == Key.Left || k.Keycode == Key.Right))
			{
				if (k.Keycode == Key.Right)
					_emojiPicker.SelectNext();
				else
					_emojiPicker.SelectPrev();
				GetViewport().SetInputAsHandled();
			}
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
			Root.Players.LocalPlayer.Character?.PlayEmote(emoteName);
			return;
		}

		RecordEmojisFromText(text);

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
			CloseEmojiPicker();
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
		return CreateNewChatLabel(from.Name, msg, from.ChatColor, from);
	}

	public UIChatLabel CreateNewChatLabel(string authorName, string content, Color? chatColor = null, Player? authorPlayer = null)
	{
		UIChatLabel chatLabel = Globals.CreateInstanceFromScene<UIChatLabel>(ChatLabelPath);
		chatLabel.AuthorName = authorName;
		chatLabel.AuthorPlayer = authorPlayer;
		chatLabel.ChatColorsEnabled = Root.PlayerDefaults.ChatColorsEnabled
			&& ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.Chat.ChatColors);
		chatLabel.FontPath = ClientSettingsService.Instance.Get<string>(ClientSettingKeys.Chat.ChatFont);
		chatLabel.FontSize = (int)ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Chat.ChatFontSize);
		if (chatColor.HasValue)
		{
			chatLabel.NameColor = chatColor.Value;
		}
		chatLabel.Content = content;
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

	public override void _Input(InputEvent @event)
	{
		if (_emojiPicker.Visible && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
		{
			Vector2 clickPos = mb.GlobalPosition;
			Rect2 pickerRect = new(_emojiPicker.GlobalPosition, _emojiPicker.Size);
			Rect2 buttonRect = new(_emojiButton.GlobalPosition, _emojiButton.Size);
			if (!pickerRect.HasPoint(clickPos) && !buttonRect.HasPoint(clickPos))
			{
				CloseEmojiPicker();
			}
		}

		if (!_isResizing) return;

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
		{
			_isResizing = false;
		}
		else if (@event is InputEventMouseMotion motion)
		{
			float newWidth = _resizeStartWidth + (motion.GlobalPosition.X - _resizeStartMousePos.X);
			newWidth = Mathf.Clamp(newWidth, _minSize.X, _resizeStartMaxWidth);
			_chatPanel.Size = new Vector2(newWidth, newWidth / AspectRatio);
		}
	}

	private void OnResizeHandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
		{
			_isResizing = true;
			_resizeStartWidth = _chatPanel.Size.X;
			_resizeStartMaxWidth = MaxChatWidth;
			_resizeStartMousePos = button.GlobalPosition;
			GetViewport().SetInputAsHandled();
		}
	}

	private void OnResizeHandleMouseEntered() => TweenResizeHandleAlpha(Colors.White, 0.15f);
	private void OnResizeHandleMouseExited() => TweenResizeHandleAlpha(new Color(1, 1, 1, 0.25f), 0.3f);

	private void TweenResizeHandleAlpha(Color target, float duration)
	{
		_resizeHandleTween?.Kill();
		_resizeHandleTween = CreateTween();
		_resizeHandleTween.TweenProperty(_resizeHandle, "modulate", target, duration);
	}

	private void ClampToViewport()
	{
		var viewSize = GetViewportRect().Size;
		float maxWidth = MaxChatWidth;

		var size = _chatPanel.Size;
		float clampedX = Mathf.Clamp(size.X, _minSize.X, maxWidth);
		float clampedY = clampedX / AspectRatio;

		if (Mathf.Abs(size.X - clampedX) > 0.01f || Mathf.Abs(size.Y - clampedY) > 0.01f)
			_chatPanel.Size = new Vector2(clampedX, clampedY);

		Vector2 pos = Position;
		float newX = Mathf.Clamp(pos.X, 0, viewSize.X - clampedX - 16);
		float newY = Mathf.Clamp(pos.Y, 0, viewSize.Y - clampedY - 16);

		if (pos.X != newX || pos.Y != newY)
			Position = new Vector2(newX, newY);
	}

	private void OnEmojiButtonPressed()
	{
		if (!_isAutocompleteOpen && _emojiPicker.Visible)
		{
			_emojiPicker.Visible = false;
			_emojiButton.ButtonPressed = false;
			return;
		}

		_isAutocompleteOpen = false;
		_emojiButton.ButtonPressed = true;
		_emojiPicker.ShowFullPicker(_chatPanel.Size.X);
		_emojiPicker.Size = new Vector2(_chatPanel.Size.X, 190);
		PositionPickerBelowField();
		_emojiPicker.Visible = true;
	}

	private void OnChatFieldTextChanged(string newText)
	{
		if (_suppressAutocomplete)
			return;

		if (_emojiPicker.Visible && !_isAutocompleteOpen)
			return;

		int cursorPos = _chatField.CaretColumn;

		int colonIdx = -1;
		int pending = -1;
		for (int i = 0; i < cursorPos; i++)
		{
			if (newText[i] == ':')
				pending = pending >= 0 ? -1 : i;
		}
		colonIdx = pending;

		if (colonIdx >= 0 && cursorPos > colonIdx + 1)
		{
			string partial = newText[(colonIdx + 1)..cursorPos];
			if (!partial.Contains(' ') && partial.Length >= 1)
			{
				var wasClosed = !_emojiPicker.Visible;
				_emojiPicker.ShowAutocomplete(partial);
				if (_emojiPicker.VisibleItemCount > 0)
				{
					_isAutocompleteOpen = true;
					_emojiButton.ButtonPressed = true;
					_emojiPicker.Size = new Vector2(_chatPanel.Size.X, 60);
					PositionPickerBelowField();
					if (wasClosed)
						_emojiPicker.Visible = true;
					return;
				}
			}
		}

		if (_isAutocompleteOpen)
		{
			CloseEmojiPicker();
		}
	}

	private void PositionPickerBelowField()
	{
		Vector2 selfGlobal = GlobalPosition;
		Vector2 panelGlobal = _chatFieldPanel.GlobalPosition;
		_emojiPicker.Position = new Vector2(
			panelGlobal.X - selfGlobal.X,
			panelGlobal.Y - selfGlobal.Y + _chatFieldPanel.Size.Y + 4
		);
	}

	private void CloseEmojiPicker()
	{
		_emojiPicker.Visible = false;
		_isAutocompleteOpen = false;
		_emojiButton.ButtonPressed = false;
	}

	private static void RecordEmojisFromText(string msg)
	{
		var builtIn = ChatService.BuiltInEmojis;
		int idx = 0;
		while ((idx = msg.IndexOf(':', idx)) >= 0)
		{
			int end = msg.IndexOf(':', idx + 1);
			if (end > idx + 1)
			{
				string name = msg[(idx + 1)..end];
				if (builtIn.ContainsKey(name))
					UIEmojiPicker.RecordEmojiUse(name);
			}
			idx = end >= 0 ? end + 1 : idx + 1;
		}
	}

	private void OnEmojiPicked(string emojiName)
	{
		InsertEmojiAtCursor(emojiName);
	}

	private void InsertEmojiAtCursor(string emojiName)
	{
		if (!Input.IsKeyPressed(Key.Shift))
			CloseEmojiPicker();

		_suppressAutocomplete = true;

		try
		{
			UIEmojiPicker.InsertEmojiAtCursor(_chatField, emojiName);
		}
		finally
		{
			_suppressAutocomplete = false;
		}
		_chatField.GrabFocus();
	}
}
