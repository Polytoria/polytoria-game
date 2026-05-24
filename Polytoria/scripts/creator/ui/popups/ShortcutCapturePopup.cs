// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Input;
using System;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class ShortcutCapturePopup : PopupWindowBase
{
	private Label _instructions = null!;
	private Button _captureButton = null!;
	private Button _okButton = null!;
	private Button _cancelButton = null!;

	public string ActionLabel { get; set; } = string.Empty;
	public CreatorKeybindBinding? InitialBinding { get; set; }

	public event Action<CreatorKeybindBinding>? Submitted;
	public event Action? Canceled;

	private CreatorKeybindBinding? _currentBinding;

	public override void _Ready()
	{
		Title = "Set Shortcut";
		Unresizable = true;
		MinSize = new Vector2I(420, 180);
		Size = new Vector2I(420, 180);

		VBoxContainer root = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		MarginContainer margin = new()
		{
		};
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		root.AddChild(margin);

		VBoxContainer layout = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		layout.AddThemeConstantOverride("separation", 12);
		margin.AddChild(layout);

		_instructions = new Label
		{
			Text = $"Press a shortcut for {ActionLabel}."
		};
		layout.AddChild(_instructions);

		_captureButton = new Button
		{
			Text = "Press keys here",
			FocusMode = Control.FocusModeEnum.All,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_captureButton.GuiInput += OnCaptureGuiInput;
		layout.AddChild(_captureButton);

		HBoxContainer actions = new()
		{
			Alignment = BoxContainer.AlignmentMode.End,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		actions.AddThemeConstantOverride("separation", 8);
		layout.AddChild(actions);

		_cancelButton = new Button
		{
			Text = "Cancel"
		};
		_cancelButton.Pressed += OnCancel;
		actions.AddChild(_cancelButton);

		_okButton = new Button
		{
			Text = "OK",
			Disabled = true
		};
		_okButton.Pressed += OnOK;
		actions.AddChild(_okButton);

		_currentBinding = InitialBinding?.Copy();
		RefreshCaptureLabel();
		_okButton.Disabled = _currentBinding == null;

		_captureButton.GrabFocus();
		base._Ready();
	}

	public override void _ExitTree()
	{
		_captureButton.GuiInput -= OnCaptureGuiInput;
		_cancelButton.Pressed -= OnCancel;
		_okButton.Pressed -= OnOK;
		base._ExitTree();
	}

	private void OnCancel()
	{
		Canceled?.Invoke();
		QueueFree();
	}

	private void OnOK()
	{
		if (_currentBinding == null)
			return;

		Submitted?.Invoke(_currentBinding.Copy());
		QueueFree();
	}

	private void OnCaptureGuiInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (IsModifierOnly(key))
			return;

		_currentBinding = new CreatorKeybindBinding()
		{
			Keycode = key.Keycode,
			CtrlPressed = key.CtrlPressed,
			ShiftPressed = key.ShiftPressed,
			AltPressed = key.AltPressed,
			MetaPressed = key.MetaPressed
		};

		RefreshCaptureLabel();
		_okButton.Disabled = false;
	}

	private void RefreshCaptureLabel()
	{
		string label = _currentBinding != null ? _currentBinding.ToDisplayString() : "Press keys here";
		_captureButton.Text = label;
		_instructions.Text = $"Press a shortcut for {ActionLabel}.";
	}

	private static bool IsModifierOnly(InputEventKey key)
	{
		return key.Keycode is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;
	}
}
