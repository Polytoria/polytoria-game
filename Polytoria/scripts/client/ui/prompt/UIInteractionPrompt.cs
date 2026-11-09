// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Prompt;

public partial class UIInteractionPrompt : Control
{
	private const float FadeSec = 0.15f;
	private const float UnfocusedAlpha = 0.5f;
	private const int MousePointer = -1;
	private const string GlyphsPath = "res://assets/textures/client/prompt/glyphs/";

	private static readonly Dictionary<string, Texture2D?> _icons = [];

	private static readonly Dictionary<KeyCodeEnum, string> _keyNames = new()
	{
		{ KeyCodeEnum.Escape, "Esc" },
		{ KeyCodeEnum.Tab, "Tab" },
		{ KeyCodeEnum.Backspace, "Backspace" },
		{ KeyCodeEnum.Enter, "Enter" },
		{ KeyCodeEnum.KpEnter, "Enter" },
		{ KeyCodeEnum.Insert, "Ins" },
		{ KeyCodeEnum.Delete, "Del" },
		{ KeyCodeEnum.Pause, "Pause" },
		{ KeyCodeEnum.Print, "PrtSc" },
		{ KeyCodeEnum.Home, "Home" },
		{ KeyCodeEnum.End, "End" },
		{ KeyCodeEnum.Left, "←" },
		{ KeyCodeEnum.Up, "↑" },
		{ KeyCodeEnum.Right, "→" },
		{ KeyCodeEnum.Down, "↓" },
		{ KeyCodeEnum.PageUp, "PgUp" },
		{ KeyCodeEnum.PageDown, "PgDn" },
		{ KeyCodeEnum.Shift, "Shift" },
		{ KeyCodeEnum.Ctrl, "Ctrl" },
		{ KeyCodeEnum.Alt, "Alt" },
		{ KeyCodeEnum.CapsLock, "Caps" },
		{ KeyCodeEnum.NumLock, "Num" },
		{ KeyCodeEnum.ScrollLock, "ScrLk" },
		{ KeyCodeEnum.Space, "Space" },
		{ KeyCodeEnum.KpMultiply, "Num *" },
		{ KeyCodeEnum.KpDivide, "Num /" },
		{ KeyCodeEnum.KpSubtract, "Num -" },
		{ KeyCodeEnum.KpAdd, "Num +" },
		{ KeyCodeEnum.KpPeriod, "Num ." },
	};

	[Export] private Control _body = null!;
	[Export] private RichTextLabel _title = null!;
	[Export] private RichTextLabel _subtitle = null!;
	[Export] private Range _progressBar = null!;
	[Export] private Label _keyLabel = null!;
	[Export] private TextureRect _keyIcon = null!;
	[Export] private Control? _keyCap;
	[Export] private bool _keyboardIcons = true;

	private InteractionPrompt? _prompt;
	private Vector3 _position;
	private int? _pointer;
	private bool _isPreview;

	public bool Focused;
	public bool Holding;
	public float PromptScale = 1f;
	public Vector2 ScreenOffset;

	public bool PointerDown => _pointer.HasValue;

	public float Progress
	{
		set => _progressBar.Value = value;
	}

	public override void _Ready()
	{
		ProcessPriority = 100;
		Modulate = new(1f, 1f, 1f, 0f);
		_body.GuiInput += OnBodyGuiInput;
		base._Ready();
	}

	public void Attach(InteractionPrompt prompt)
	{
		_prompt = prompt;
		_position = prompt.PromptNode.GlobalPosition;
	}

	public void ShowAsPreview()
	{
		_isPreview = true;
		Focused = true;
		_body.MouseFilter = MouseFilterEnum.Ignore;
	}

	public void Detach()
	{
		_prompt = null;
		_pointer = null;
	}

	public void SetText(string title, string subtitle)
	{
		_title.Text = title;
		_subtitle.Text = subtitle;
		_subtitle.Visible = !string.IsNullOrWhiteSpace(_subtitle.GetParsedText());
	}

	public void SetKey(KeyCodeEnum key, bool touch)
	{
		string label = GetKeyLabel(key);
		Texture2D? icon = touch ? LoadIcon("touch.svg") : GetKeyIcon(key, _keyboardIcons || label.Length > 1);
		_keyIcon.Texture = icon;
		_keyIcon.Visible = icon != null;
		_keyLabel.Text = label;
		_keyLabel.Visible = icon == null;
		_keyCap?.Visible = icon == null;
	}

	public override void _Process(double delta)
	{
		if (_prompt != null)
		{
			_position = _prompt.PromptNode.GlobalPosition;
		}

		Camera3D? camera = GetViewport().GetCamera3D();
		bool outOfRange = _isPreview && _prompt != null && camera != null && camera.GlobalPosition.DistanceTo(_position) > _prompt.MaxDistance;

		float targetAlpha = _prompt == null || outOfRange ? 0f : Focused ? 1f : UnfocusedAlpha;
		float alpha = Mathf.MoveToward(Modulate.A, targetAlpha, (float)delta / FadeSec);
		Modulate = new(1f, 1f, 1f, alpha);

		if (_prompt == null && alpha <= 0f)
		{
			QueueFree();
			return;
		}

		if (camera == null || camera.IsPositionBehind(_position))
		{
			Visible = false;
			return;
		}
		Visible = true;

		Color keyColor = Holding ? new(0.75f, 0.75f, 0.75f) : Colors.White;
		_keyCap?.SelfModulate = keyColor;
		_keyIcon.SelfModulate = keyColor;

		Vector2 size = _body.GetCombinedMinimumSize();
		_body.Size = size;
		PivotOffset = size / 2f;
		Scale = new(PromptScale, PromptScale);
		Position = (camera.UnprojectPosition(_position) - size / 2f + ScreenOffset).Round();
	}

	private void OnBodyGuiInput(InputEvent @event)
	{
		if (_prompt == null || _pointer.HasValue) return;

		if (@event is InputEventScreenTouch { Pressed: true } touch)
		{
			_pointer = touch.Index;
			_body.AcceptEvent();
		}
		else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _prompt.Clickable)
		{
			_pointer = MousePointer;
			_body.AcceptEvent();
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!_pointer.HasValue) return;

		if (@event is InputEventScreenTouch { Pressed: false } touch && touch.Index == _pointer
			|| @event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && _pointer == MousePointer)
		{
			_pointer = null;
		}
	}

	private static Texture2D? GetKeyIcon(KeyCodeEnum key, bool keyboard)
	{
		if (InputService.IsGamepadKey(key))
		{
			return LoadIcon($"{GetGamepadFolder()}/{key}.svg") ?? LoadIcon($"xbox/{key}.svg");
		}
		if (key is >= KeyCodeEnum.MouseLeft and <= KeyCodeEnum.MouseXbutton2 or KeyCodeEnum.MouseAxisX or KeyCodeEnum.MouseAxisY)
		{
			return LoadIcon($"mouse/{key}.svg");
		}
		return keyboard ? LoadIcon($"keyboard/{GetLayoutKey(key)}.svg") : null;
	}

	public static string GetKeyLabel(KeyCodeEnum key)
	{
		if (_keyNames.TryGetValue(key, out string? name)) return name;
		if (key is >= KeyCodeEnum.Kp0 and <= KeyCodeEnum.Kp9) return "Num " + (key - KeyCodeEnum.Kp0);
		if (key is >= KeyCodeEnum.GamepadA and < KeyCodeEnum.Special) return key.ToString();

		string text = OS.GetKeycodeString((Key)GetLayoutKey(key));
		return string.IsNullOrEmpty(text) ? key.ToString() : text.ToUpperInvariant();
	}

	private static KeyCodeEnum GetLayoutKey(KeyCodeEnum key)
	{
		if (key >= KeyCodeEnum.Special) return key;

		Key label = DisplayServer.KeyboardGetLabelFromPhysical((Key)key);
		return label == Key.None ? key : (KeyCodeEnum)label;
	}

	private static string GetGamepadFolder()
	{
		Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
		if (pads.Count == 0) return "xbox";

		string name = Input.GetJoyName(pads[0]).ToLowerInvariant();
		if (name.Contains("playstation") || name.Contains("dualshock") || name.Contains("dualsense") || name.Contains("ps4") || name.Contains("ps5"))
		{
			return "playstation";
		}
		if (name.Contains("nintendo") || name.Contains("switch") || name.Contains("joy-con"))
		{
			return "switch";
		}
		return "xbox";
	}

	private static Texture2D? LoadIcon(string file)
	{
		string path = GlyphsPath + file;
		if (!_icons.TryGetValue(path, out Texture2D? icon))
		{
			icon = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
			_icons[path] = icon;
		}
		return icon;
	}
}
