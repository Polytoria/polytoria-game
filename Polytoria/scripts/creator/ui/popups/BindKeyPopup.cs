// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class BindKeyPopup : PopupWindowBase
{
	[Export] private Button _bindBtn = null!;
	[Export] private OptionButton _keyTypeOpt = null!;
	[Export] private LineEdit _searchEdit = null!;
	[Export] private Tree _viewTree = null!;
	[Export] private Button _okBtn = null!;
	[Export] private Button _cancelBtn = null!;

	private readonly Dictionary<KeyCodeEnum, TreeItem> _keycodeToItem = [];
	private readonly Dictionary<TreeItem, KeyCodeEnum> _itemToKeycode = [];

	public event Action<(KeyCodeEnum Key, KeyTypeEnum Type)>? KeyBinded;
	public event Action? Canceled;

	public override void _Ready()
	{
		base._Ready();
		_bindBtn.GrabFocus();
		_cancelBtn.Pressed += OnCancel;

		_bindBtn.GuiInput += OnBindGuiInput;
		_okBtn.Pressed += OnOK;

		TreeItem root = _viewTree.CreateItem();
		bool isFirst = true;

		foreach (string k in Enum.GetNames<KeyCodeEnum>())
		{
			KeyCodeEnum v = Enum.Parse<KeyCodeEnum>(k);
			TreeItem ch = root.CreateChild();
			ch.SetText(0, k);
			ch.SetSelectable(0, true);
			_keycodeToItem[v] = ch;
			_itemToKeycode[ch] = v;

			if (isFirst)
			{
				ch.Select(0);
				isFirst = false;
			}
		}
	}

	public override void _ExitTree()
	{
		_cancelBtn.Pressed -= OnCancel;
		_bindBtn.GuiInput -= OnBindGuiInput;
		_okBtn.Pressed -= OnOK;

		base._ExitTree();
	}

	private void OnCancel()
	{
		Canceled?.Invoke();
		QueueFree();
	}

	private void OnOK()
	{
		if (_itemToKeycode.TryGetValue(_viewTree.GetSelected(), out KeyCodeEnum val))
		{
			KeyBinded?.Invoke((val, (KeyTypeEnum)_keyTypeOpt.Selected));
		}
		QueueFree();
	}

	private void OnBindGuiInput(InputEvent @event)
	{
		KeyCodeEnum? k = InputService.InputEventToKeyCode(@event);
		if (k.HasValue)
		{
			if (_keycodeToItem.TryGetValue(k.Value, out TreeItem? ch))
			{
				_bindBtn.Text = k.Value.ToString();
				_viewTree.DeselectAll();
				ch.Select(0);
				_viewTree.ScrollToItem(ch, true);
			}
		}
	}
}
