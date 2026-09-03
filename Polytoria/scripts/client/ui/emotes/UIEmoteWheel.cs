// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public partial class UIEmoteWheel : Control
{
	public bool EmoteWheelActive = false;
	public const string EmoteIconPath = "res://assets/textures/client/ui/emotes/icons/";
	private const string EmoteItemPath = "res://scenes/client/ui/emotes/emote_item.tscn";
	private const string EmoteDividerPath = "res://scenes/client/ui/emotes/emote_divider.tscn";

	private PackedScene _emoteItemPacked = null!;
	private PackedScene _dividerPacked = null!;

	[Export] private Control _emoteCursor = null!;
	[Export] private Control _emotePivot = null!;
	[Export] private Control _emoteTarget = null!;
	[Export] private Control _emoteContainer = null!;
	[Export] private Button _closeButton = null!;
	[Export] private AnimationPlayer _animPlay = null!;

	private readonly Dictionary<string, UIEmoteItem> _keyToItem = [];
	private UIEmoteItem? _oldItem = null;
	private bool _hoveringClose = false;

	public bool UseEmoteWheel = true;

	public override void _Ready()
	{
		_emoteItemPacked = GD.Load<PackedScene>(EmoteItemPath);
		_dividerPacked = GD.Load<PackedScene>(EmoteDividerPath);
		PlaceEmotes();
		PlaceDividers();

		_closeButton.MouseEntered += OnCloseMouseEntered;
		_closeButton.MouseExited += OnCloseMouseExited;
	}

	private void OnCloseMouseEntered()
	{
		_hoveringClose = true;
		_emoteCursor.Visible = false;
	}

	private void OnCloseMouseExited()
	{
		_hoveringClose = false;
		_emoteCursor.Visible = true;
	}

	public void ToggleEmoteWheel()
	{
		if (!UseEmoteWheel) return;
		EmoteWheelActive = !EmoteWheelActive;
		if (EmoteWheelActive)
		{
			OpenEmoteWheel();
		}
		else
		{
			CloseEmoteWheel();
		}
	}

	public void OpenEmoteWheel()
	{
		if (!UseEmoteWheel) return;
		GrabFocus();
		EmoteWheelActive = true;
		_oldItem?.IsActive = false;
		_oldItem = null;
		_animPlay.Play("open");
	}

	public void CloseEmoteWheel()
	{
		ReleaseFocus();
		EmoteWheelActive = false;
		_animPlay.Play("close");
	}

	private void PlaceEmotes()
	{
		int count = Player.EmoteWheelList.Length;
		if (count == 0) return;
		float step = Mathf.Tau / count;
		for (int i = 0; i < count; i++)
		{
			_emotePivot.Rotation = Mathf.Pi + (step * i);
			AddEmoteAtTarget(Player.EmoteWheelList[i]);
		}
	}

	private void PlaceDividers()
	{
		int count = Player.EmoteWheelList.Length;
		if (count < 2 || _dividerPacked == null) return;
		float step = Mathf.Tau / count;
		for (int i = 0; i < count; i++)
		{
			// Divider angle = halfway between emotes, starting at 180 degrees
			float angle = Mathf.Pi + (step * i) + step / 2f;
			Control divider = _dividerPacked.Instantiate<Control>();
			_emoteContainer.AddChild(divider);

			// Place at pivot
			divider.GlobalPosition = _emotePivot.GlobalPosition;
			divider.Rotation = angle;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_accept") || @event.IsActionPressed("lmb"))
		{
			if (!EmoteWheelActive) return;
			if (!_hoveringClose)
			{
				string selected = GetSelectedEmoteName();
				CoreUIRoot.Singleton.Root.Players.LocalPlayer.Character?.PlayEmote(selected);
			}
			CloseEmoteWheel();
		}
		if (@event.IsActionPressed("toggle_emote"))
		{
			if (!CoreUIRoot.Singleton.Root.Input.IsGameFocused && !EmoteWheelActive) return;
			ToggleEmoteWheel();
		}
		base._Input(@event);
	}

	// Add emote at _emoteTarget's global position
	private void AddEmoteAtTarget(string emoteName)
	{
		UIEmoteItem emoteItem = _emoteItemPacked.Instantiate<UIEmoteItem>();
		emoteItem.EmoteName = emoteName;
		_emoteContainer.AddChild(emoteItem);
		emoteItem.GlobalPosition = _emoteTarget.GlobalPosition;
		_keyToItem[emoteName] = emoteItem;
	}

	private float GetCursorAngle()
	{
		Vector2 dir = GetGlobalMousePosition() - _emotePivot.GlobalPosition;
		float angle = Mathf.Atan2(dir.Y, dir.X);
		return angle;
	}

	private int GetSelectedEmoteIndex()
	{
		float angle = GetCursorAngle();

		// Rotate by 45deg
		angle -= Mathf.Pi / 4f;

		if (angle < 0)
			angle += Mathf.Tau;

		int count = Player.EmoteWheelList.Length;
		float step = Mathf.Tau / count;

		int index = Mathf.FloorToInt(angle / step);
		return Mathf.Clamp(index, 0, count - 1);
	}

	private string GetSelectedEmoteName()
	{
		int index = GetSelectedEmoteIndex();
		return Player.EmoteWheelList[index];
	}

	private UIEmoteItem? GetSelectedEmoteItem()
	{
		string name = GetSelectedEmoteName();
		if (_keyToItem.TryGetValue(name, out UIEmoteItem? item))
		{
			return item;
		}
		return null;
	}

	public override void _Process(double delta)
	{
		if (!EmoteWheelActive) { return; }
		float angle = GetCursorAngle();

		_emoteCursor.Rotation = angle;

		UIEmoteItem? item = GetSelectedEmoteItem();

		if (item != null && item != _oldItem)
		{
			_oldItem?.IsActive = false;
			if (!_hoveringClose)
			{
				item.IsActive = true;
				_oldItem = item;
			}
			else
			{
				_oldItem = null;
			}
		}
	}
}
