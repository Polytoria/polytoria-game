// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Datamodel.Data;
using Polytoria.Enums;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using static Polytoria.Datamodel.Environment;

namespace Polytoria.Datamodel.Services;

[Static("Input"), ExplorerExclude, SaveIgnore]
public sealed partial class InputService : Instance
{
	private bool _cursorLocked = false;
	private bool _cursorVisible = true;
	private InputMapData _mapData = new();

	internal InputMapData MapData
	{
		get
		{
#if CREATOR
			if (Root.LinkedSession != null)
			{
				return Root.LinkedSession.InputMap;
			}
#endif
			return _mapData;
		}
		set
		{
			_mapData = value;
		}
	}

	[ScriptProperty] public bool IsWindowFocused { get; private set; } = true;
	[ScriptProperty] public bool IsTouchscreen { get; private set; } = false;
	[ScriptProperty] public bool IsGameFocused { get; internal set; } = false;
	[ScriptProperty] public bool IsInputFocused => !IsGameFocused;
	[ScriptProperty] public bool IsGamepadConnected { get; private set; } = false;
	[ScriptProperty] public bool IsMenuOpened { get; internal set; } = false;

	[ScriptProperty]
	public bool CursorLocked
	{
		get => _cursorLocked;
		set
		{
			_cursorLocked = value;
			RecomputeMouseMode();
		}
	}
	[ScriptProperty]
	public bool CursorVisible
	{
		get => _cursorVisible;
		set
		{
			_cursorVisible = value;
			RecomputeMouseMode();
		}
	}

	[ScriptProperty] public Vector2 MousePosition => OverrideMousePos ? OverrideMousePosTo : GDNode.GetViewport().GetMousePosition();
	[ScriptLegacyProperty("MousePosition")] public Vector3 LegacyMousePosition => new(MousePosition.X, ScreenHeight - MousePosition.Y, 0);
	[ScriptProperty] public int ScreenWidth => (int)GDNode.GetViewport().GetVisibleRect().Size.X;
	[ScriptProperty] public int ScreenHeight => (int)GDNode.GetViewport().GetVisibleRect().Size.Y;

	internal bool OverrideMousePos { get; set; }
	internal Vector2 OverrideMousePosTo { get; set; }

	[ScriptProperty] public PTSignal GameFocused { get; private set; } = new();
	[ScriptProperty] public PTSignal GameUnfocused { get; private set; } = new();
	[ScriptProperty] public PTSignal GamepadConnected { get; private set; } = new();
	[ScriptProperty] public PTSignal GamepadDisconnected { get; private set; } = new();

	[ScriptProperty] public PTSignal<KeyCodeEnum, bool> KeyDown { get; private set; } = new();
	[ScriptProperty] public PTSignal<KeyCodeEnum, bool> KeyUp { get; private set; } = new();
	[ScriptProperty] public PTSignal<KeyCodeEnum, float> AxisValueChanged { get; private set; } = new();
	[ScriptLegacyProperty("KeyDown")] public PTSignal LegacyKeyDown { get; private set; } = new();
	[ScriptLegacyProperty("KeyUp")] public PTSignal LegacyKeyUp { get; private set; } = new();

	internal event Action<InputEvent>? GodotInputEvent;

	public readonly Dictionary<Key, string> CompatKeyCodeMapping = new()
	{
		{Key.None, "None"},
		{Key.Backspace, "Backspace"},
		{Key.Tab, "Tab"},
		{Key.Clear, "Clear"},
		{Key.Enter, "Return"},
		{Key.Pause, "Pause"},
		{Key.Escape, "Escape"},
		{Key.Space, "Space"},
		{Key.Exclam, "Exclaim"},
		{Key.Dollar, "Dollar"},
		{Key.Ampersand, "Ampersand"},
		{Key.Asterisk, "Asterisk"},
		{Key.Plus, "Plus"},
		{Key.Comma, "Comma"},
		{Key.Minus, "Minus"},
		{Key.Period, "Period"},
		{Key.Slash, "Slash"},
		{Key.Key0, "0"},
		{Key.Key1, "1"},
		{Key.Key2, "2"},
		{Key.Key3, "3"},
		{Key.Key4, "4"},
		{Key.Key5, "5"},
		{Key.Key6, "6"},
		{Key.Key7, "7"},
		{Key.Key8, "8"},
		{Key.Key9, "9"},
		{Key.Colon, "Colon"},
		{Key.Semicolon, "Semicolon"},
		{Key.Less, "Less"},
		{Key.Equal, "Equals"},
		{Key.Greater, "Greater"},
		{Key.Question, "Question"},
		{Key.At, "At"},
		{Key.Bracketleft, "LeftBracket"},
		{Key.Backslash, "Backslash"},
		{Key.Bracketright, "RightBracket"},
		{Key.Underscore, "Underscore"},
		{Key.Quoteleft, "BackQuote"},
		{Key.A, "A"},
		{Key.B, "B"},
		{Key.C, "C"},
		{Key.D, "D"},
		{Key.E, "E"},
		{Key.F, "F"},
		{Key.G, "G"},
		{Key.H, "H"},
		{Key.I, "I"},
		{Key.J, "J"},
		{Key.K, "K"},
		{Key.L, "L"},
		{Key.M, "M"},
		{Key.N, "N"},
		{Key.O, "O"},
		{Key.P, "P"},
		{Key.Q, "Q"},
		{Key.R, "R"},
		{Key.S, "S"},
		{Key.T, "T"},
		{Key.U, "U"},
		{Key.V, "V"},
		{Key.W, "W"},
		{Key.X, "X"},
		{Key.Y, "Y"},
		{Key.Z, "Z"},
		{Key.Delete, "Delete"},
		{Key.Kp0, "Keypad0"},
		{Key.Kp1, "Keypad1"},
		{Key.Kp2, "Keypad2"},
		{Key.Kp3, "Keypad3"},
		{Key.Kp4, "Keypad4"},
		{Key.Kp5, "Keypad5"},
		{Key.Kp6, "Keypad6"},
		{Key.Kp7, "Keypad7"},
		{Key.Kp8, "Keypad8"},
		{Key.Kp9, "Keypad9"},
		{Key.KpPeriod, "KeypadPeriod"},
		{Key.KpDivide, "KeypadDivide"},
		{Key.KpMultiply, "KeypadMultiply"},
		{Key.KpSubtract, "KeypadMinus"},
		{Key.KpAdd, "KeypadPlus"},
		{Key.KpEnter, "KeypadEnter"},
		{Key.Up, "UpArrow"},
		{Key.Down, "DownArrow"},
		{Key.Right, "RightArrow"},
		{Key.Left, "LeftArrow"},
		{Key.Insert, "Insert"},
		{Key.Home, "Home"},
		{Key.End, "End"},
		{Key.Pageup, "PageUp"},
		{Key.Pagedown, "PageDown"},
		{Key.F1, "F1"},
		{Key.F2, "F2"},
		{Key.F3, "F3"},
		{Key.F4, "F4"},
		{Key.F5, "F5"},
		{Key.F6, "F6"},
		{Key.F7, "F7"},
		{Key.F8, "F8"},
		{Key.F9, "F9"},
		{Key.F10, "F10"},
		{Key.F11, "F11"},
		{Key.F12, "F12"},
		{Key.F13, "F13"},
		{Key.F14, "F14"},
		{Key.F15, "F15"},
		{Key.Numlock, "Numlock"},
		{Key.Capslock, "CapsLock"},
		{Key.Scrolllock, "ScrollLock"},
		{Key.Shift, "LeftShift"},
		{Key.Ctrl, "LeftControl"},
		{Key.Alt, "LeftAlt"},
		{Key.Help, "Help"},
		{Key.Print, "Print"},
		{Key.Menu, "Menu"},
	};

	public readonly Dictionary<MouseButton, string> CompatMouseButtonMapping = new()
	{
		{MouseButton.Left, "Mouse0" },
		{MouseButton.Right, "Mouse1" },
		{MouseButton.Middle, "Mouse2" },
	};

	private readonly Dictionary<string, bool> _legacyKeydowns = [];
	private readonly Dictionary<string, bool> _legacyFrameKeydowns = [];
	private readonly Dictionary<KeyCodeEnum, bool> _keyStates = [];
	private readonly Dictionary<KeyCodeEnum, float> _keyWeight = [];
	private readonly Dictionary<MouseButton, bool> _mouseBtnDown = [];
	private readonly Dictionary<MouseButton, bool> _mouseFrameBtnDown = [];
	private float _mouseScrollDelta = 0;

	public override void Init()
	{
		if (Globals.IsMobileBuild || OS.HasFeature("touchscreen"))
		{
			IsTouchscreen = true;
		}

		if (Input.GetConnectedJoypads().Count > 0)
		{
			OnGamepadConnected();
		}

		if (Root != null && Root.Network != null)
		{
			if (Root.SessionType == World.SessionTypeEnum.Client)
			{
				SetupCursors();
			}
			if (Root.Network.IsServer)
			{
				Root.Network.PeerPreInit += OnPeerPreInit;
			}

#if CREATOR
			if (Root.Container != null)
			{
				Root.Container.GodotInputEvent += OnInput;
			}
			else
			{
				Globals.GodotInputEvent += OnInput;
			}
#else
			Globals.GodotInputEvent += OnInput;
#endif
		}

		RenderingServer.FramePostDraw += ClearInputFrames;

		Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
		Globals.GodotNotification += OnNotification;

		SetProcess(true);

		base.Init();
	}

	public override void PreDelete()
	{
#if CREATOR
		if (Root.Container != null)
		{
			Root.Container.GodotInputEvent -= OnInput;
		}
		else
		{
			Globals.GodotInputEvent -= OnInput;
		}
#else
		Globals.GodotInputEvent -= OnInput;
#endif
		if (Root.Network.IsServer)
		{
			Root.Network.PeerPreInit -= OnPeerPreInit;
		}
		RenderingServer.FramePostDraw -= ClearInputFrames;
		Globals.GodotNotification -= OnNotification;
		Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
		base.PreDelete();
	}

	private static void SetupCursors()
	{
		Input.SetCustomMouseCursor(GD.Load<Image>("res://assets/textures/client/cursor/arrow.png"), Input.CursorShape.Arrow);
		Input.SetCustomMouseCursor(GD.Load<Image>("res://assets/textures/client/cursor/click.png"), Input.CursorShape.PointingHand);
		Input.SetCustomMouseCursor(GD.Load<Image>("res://assets/textures/client/cursor/grab.png"), Input.CursorShape.Drag);
		Input.SetCustomMouseCursor(GD.Load<Image>("res://assets/textures/client/cursor/grabbing.png"), Input.CursorShape.CanDrop);
	}

	private void OnPeerPreInit(int peerID)
	{
		RpcId(peerID, nameof(NetRecvKeyMap), MapData.SaveToString());
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvKeyMap(string rawMap)
	{
		MapData = InputMapData.LoadFromString(rawMap);
	}

	private void OnJoyConnectionChanged(long device, bool connected)
	{
		if (device == 0)
		{
			if (connected)
			{
				OnGamepadConnected();
			}
			else
			{
				OnGamepadDisconnected();
			}
		}
	}

	private void OnGamepadConnected()
	{
		IsGamepadConnected = true;
		GamepadConnected?.Invoke();
	}

	private void OnGamepadDisconnected()
	{
		IsGamepadConnected = false;
		GamepadDisconnected?.Invoke();
	}

	private void OnNotification(int noti)
	{
		if (noti == Node.NotificationWMWindowFocusIn)
		{
			IsWindowFocused = true;
		}
		else if (noti == Node.NotificationWMWindowFocusOut)
		{
			IsWindowFocused = false;
		}
	}

	public override void Process(double delta)
	{
		bool newFocused = RecomputeGameFocused();
		if (newFocused != IsGameFocused)
		{
			IsGameFocused = newFocused;
			if (newFocused)
			{
				GameFocused?.Invoke();
			}
			else
			{
				_keyStates.Clear();
				_keyWeight.Clear();
				GameUnfocused?.Invoke();
			}
			RecomputeMouseMode();
		}
		ProcessInputs();
		base.Process(delta);
	}

	private void ClearInputFrames()
	{
		_legacyFrameKeydowns.Clear();
		_mouseFrameBtnDown.Clear();
		_mouseScrollDelta = 0;
	}

	private void RecomputeMouseMode()
	{
		if (!IsGameFocused || !IsWindowFocused)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			return;
		}
		Input.MouseMode = _cursorLocked ? Input.MouseModeEnum.Captured : (_cursorVisible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden);
	}

	private bool RecomputeGameFocused()
	{
		if (!IsWindowFocused) return false;
		if (IsMenuOpened) return false;
#if CREATOR
		if (Root.CreatorContext != null) return Root.CreatorContext.IsViewportFocused;
#endif
		if (Root.CoreUI != null && Root.CoreUI.CoreUI != null && Root.CoreUI.CoreUI.CoreUIActive) return false;
		if (Root.Entry != null && !Root.Entry.IsFocused) return false;
		if (Root.SessionType == World.SessionTypeEnum.Creator) return false;
		if (Root.Network.IsServer) return false;
		Control? focusOwner = GDNode.GetViewport().GuiGetFocusOwner();
		if (focusOwner != null)
		{
			if (focusOwner is InputFallbackBase)
			{
				return true;
			}
		}
		return focusOwner == null;
	}

	public void OnInput(Godot.InputEvent @event)
	{
		if (@event.IsEcho()) return;
		if (IsGameFocused)
		{
			GodotInputEvent?.Invoke(@event);
		}

		KeyCodeEnum? btnEnumPre = InputEventToKeyCode(@event);
		if (!btnEnumPre.HasValue) return;
		KeyCodeEnum btnEnum = btnEnumPre.Value;

		if (@event is InputEventKey key)
		{
			if (Enum.TryParse(key.Keycode.ToString(), false, out KeyCodeEnum keyVal))
			{
				_keyStates[keyVal] = key.Pressed;
				if (key.Pressed)
				{
					KeyDown.Invoke(keyVal, IsGameFocused);
				}
				else
				{
					KeyUp.Invoke(keyVal, IsGameFocused);
				}
			}

			if (CompatKeyCodeMapping.TryGetValue(key.Keycode, out string? btn))
			{
				if (key.Pressed)
				{
					LegacyKeyDown.Invoke(btn);
				}
				else
				{
					LegacyKeyUp.Invoke(btn);
				}

				_legacyKeydowns[btn] = key.Pressed;
				_legacyFrameKeydowns[btn] = key.Pressed;
			}
		}
		else if (@event is InputEventJoypadButton joypadBtn)
		{
			_keyStates[btnEnum] = joypadBtn.Pressed;
			if (joypadBtn.Pressed)
			{
				KeyDown.Invoke(btnEnum, IsGameFocused);
			}
			else
			{
				KeyUp.Invoke(btnEnum, IsGameFocused);
			}
		}
		else if (@event is InputEventJoypadMotion joypadMotion)
		{
			float axisVal = joypadMotion.AxisValue;

			_keyWeight[btnEnum] = axisVal;
			AxisValueChanged.Invoke(btnEnum, axisVal);
		}
		else if (@event is InputEventMouseButton mouseBtn)
		{
			_keyStates[btnEnum] = mouseBtn.Pressed;
			if (mouseBtn.Pressed)
			{
				KeyDown.Invoke(btnEnum, IsGameFocused);
			}
			else
			{
				KeyUp.Invoke(btnEnum, IsGameFocused);
			}

			if (CompatMouseButtonMapping.TryGetValue(mouseBtn.ButtonIndex, out string? btn))
			{
				if (mouseBtn.Pressed)
				{
					LegacyKeyDown.Invoke(btn);
				}
				else
				{
					LegacyKeyUp.Invoke(btn);
				}

				_legacyKeydowns[btn] = mouseBtn.Pressed;
				_legacyFrameKeydowns[btn] = mouseBtn.Pressed;
				_mouseBtnDown[mouseBtn.ButtonIndex] = mouseBtn.Pressed;
				_mouseFrameBtnDown[mouseBtn.ButtonIndex] = mouseBtn.Pressed;
			}

			if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
			{
				_mouseScrollDelta = mouseBtn.Factor;
			}
			else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
			{
				_mouseScrollDelta = -mouseBtn.Factor;
			}
		}
	}

	public static KeyCodeEnum? InputEventToKeyCode(InputEvent @event)
	{
		if (@event is InputEventKey key)
		{
			if (Enum.TryParse(key.Keycode.ToString(), false, out KeyCodeEnum keyVal))
			{
				return keyVal;
			}
		}
		else if (@event is InputEventJoypadButton joypadBtn)
		{
			if (Enum.TryParse("Gamepad" + joypadBtn.ButtonIndex, false, out KeyCodeEnum btnEnum))
			{
				return btnEnum;
			}
		}
		else if (@event is InputEventJoypadMotion joypadMotion)
		{
			if (Enum.TryParse("GamepadAxis" + joypadMotion.Axis, false, out KeyCodeEnum btnEnum))
			{
				return btnEnum;
			}
		}
		else if (@event is InputEventMouseButton mouseBtn)
		{
			if (Enum.TryParse("Mouse" + mouseBtn.ButtonIndex, false, out KeyCodeEnum btnEnum))
			{
				return btnEnum;
			}
		}
		return null;
	}

	[ScriptMethod]
	public Vector3 GetMouseWorldPosition(Instance[]? ignoreList = null)
	{
		Viewport viewport = GDNode.GetViewport();
		Camera3D camera = viewport.GetCamera3D();
		if (camera == null || viewport == null)
			return Vector3.Zero;

		Vector2 mousePos = MousePosition;
		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
		Vector3 rayDir = camera.ProjectRayNormal(mousePos);

		RayResult? hit = Root.Environment.Raycast(rayOrigin, rayDir, ignoreList: ignoreList);
		return hit != null ? hit.Value.Position : rayOrigin + rayDir * 1000f;
	}

	[ScriptMethod]
	public InputActionVector2 GetVector2(string actionName)
	{
		InputAction? action = MapData.FindAction(actionName);
		if (action is InputActionVector2 v2)
		{
			InitializeInputAction(action);
			return v2;
		}
		else
		{
			throw new Exception("Input Action is not defined/is the wrong type (" + actionName + ")");
		}
	}

	[ScriptMethod]
	public InputActionButton GetButton(string actionName)
	{
		InputAction? action = MapData.FindAction(actionName);
		if (action is InputActionButton ab)
		{
			InitializeInputAction(action);
			return ab;
		}
		else
		{
			throw new Exception("Input Action is not defined/is the wrong type (" + actionName + ")");
		}
	}

	[ScriptMethod]
	public InputActionAxis GetAxis(string actionName)
	{
		InputAction? action = MapData.FindAction(actionName);
		if (action is InputActionAxis ax)
		{
			InitializeInputAction(action);
			return ax;
		}
		else
		{
			throw new Exception("Input Action is not defined/is the wrong type (" + actionName + ")");
		}
	}

	[ScriptMethod]
	public InputActionButton BindButton(string name)
	{
		return MapData.BindButton(name);
	}

	[ScriptMethod]
	public InputActionAxis BindAxis(string name)
	{
		return MapData.BindAxis(name);
	}

	[ScriptMethod]
	public InputActionVector2 BindVector2(string name)
	{
		return MapData.BindVector2(name);
	}

	private void InitializeInputAction(InputAction a)
	{
		a.InputService = this;
	}

	private void ProcessInputs()
	{
		if (Root != null && Root.Network.IsServer) return;
		if (!IsGameFocused) return;
		foreach (InputAction a in MapData.Actions)
		{
			if (a is InputActionButton btn)
			{
				// Process button actions
				bool pressed = false;
				float weight = 0;
				foreach (InputButton item in btn.Buttons)
				{
					if (IsKeyPressed(item.KeyCode))
					{
						pressed = true;
						weight = GetKeyWeight(item.KeyCode);
						break;
					}
				}

				btn.Weight = weight;
				if (btn.IsPressed != pressed)
				{
					btn.IsPressed = pressed;

					if (pressed)
					{
						btn.Pressed.Invoke();
					}
					else
					{
						btn.Released.Invoke();
					}
				}
			}
			else if (a is InputActionAxis axis)
			{
				// Process axis actions
				float pos = 0;
				float neg = 0;

				// Positive
				foreach (InputButton item in axis.Positive)
				{
					pos = GetKeyWeight(item.KeyCode);
					if (pos > 0)
					{
						break;
					}
				}

				// Negative
				foreach (InputButton item in axis.Negative)
				{
					neg = GetKeyWeight(item.KeyCode);
					if (neg > 0)
					{
						break;
					}
				}

				axis.Value = Mathf.Clamp(pos - neg, -1f, 1f);
			}
			else if (a is InputActionVector2 v2)
			{
				// Process Vector2 actions
				float up = 0;
				float down = 0;
				float left = 0;
				float right = 0;

				foreach (InputButton item in v2.Up)
				{
					up = GetKeyWeight(item.KeyCode);
					if (up > 0)
					{
						break;
					}
				}

				foreach (InputButton item in v2.Down)
				{
					down = GetKeyWeight(item.KeyCode);
					if (down > 0)
					{
						break;
					}
				}

				foreach (InputButton item in v2.Left)
				{
					left = GetKeyWeight(item.KeyCode);
					if (left > 0)
					{
						break;
					}
				}

				foreach (InputButton item in v2.Right)
				{
					right = GetKeyWeight(item.KeyCode);
					if (right > 0)
					{
						break;
					}
				}

				Vector2 finalVal = new(right - left, up - down);

				if (finalVal.LengthSquared() > 1f)
				{
					finalVal = finalVal.Normalized();
				}

				v2.Value = finalVal;
			}
		}
	}

	public bool IsKeyPressed(KeyCodeEnum keyCode)
	{
		if (_keyWeight.TryGetValue(keyCode, out float weight))
		{
			if (weight > 0.5f)
			{
				return true;
			}
		}
		if (_keyStates.TryGetValue(keyCode, out bool state)) return state;
		return false;
	}

	public float GetKeyWeight(KeyCodeEnum keyCode)
	{
		if (_keyWeight.TryGetValue(keyCode, out float state)) return state;
		return IsKeyPressed(keyCode) ? 1 : 0;
	}

	[ScriptLegacyMethod("GetMouseWorldPosition")]
	public Vector3 LegacyGetMouseWorldPosition(object? _ = null)
	{
		return GetMouseWorldPosition();
	}

	[ScriptLegacyMethod("GetMouseWorldPoint")]
	public Vector3 LegacyGetMouseWorldPoint(object? _ = null)
	{
		Viewport viewport = GDNode.GetViewport();
		Camera3D camera = viewport.GetCamera3D();
		if (camera == null || viewport == null)
			return Vector3.Zero;

		Vector2 mousePos = viewport.GetMousePosition();

		float z = camera.Near;

		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
		Vector3 rayDir = camera.ProjectRayNormal(mousePos);

		return (rayOrigin + rayDir * z);
	}

	[ScriptLegacyMethod("ScreenToWorldPoint")]
	public Vector3 LegacyScreenToWorldPoint(Vector3 pos)
	{
		Viewport viewport = GDNode.GetViewport();
		Camera3D camera = viewport.GetCamera3D();
		if (camera == null || viewport == null)
			return Vector3.Zero;

		Vector3 rayOrigin = camera.ProjectRayOrigin(new Vector2(pos.X, pos.Y));
		Vector3 rayDir = camera.ProjectRayNormal(new Vector2(pos.X, pos.Y));
		return (rayOrigin + rayDir * pos.Z);
	}

	[ScriptLegacyMethod("ScreenToViewportPoint")]
	public Vector3 LegacyScreenToViewportPoint(Vector3 pos)
	{
		Viewport viewport = GDNode.GetViewport();
		if (viewport == null)
			return Vector3.Zero;

		Vector2 size = viewport.GetVisibleRect().Size;
		return new(pos.X / size.X, pos.Y / size.Y, pos.Z);
	}

	[ScriptLegacyMethod("WorldToScreenPoint")]
	public Vector3 LegacyWorldToScreenPoint(Vector3 pos)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		Vector2 unprojected = Root.Environment.CurrentCamera.WorldToScreenPoint(pos);
		return new(unprojected.X, unprojected.Y, pos.Z);
	}

	[ScriptLegacyMethod("WorldToViewportPoint")]
	public Vector3 LegacyWorldToViewportPoint(Vector3 pos)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		Vector2 size = Root.Environment.CurrentCamera!.WorldToViewportPoint(pos);
		return new(size.X, size.Y, pos.Z);
	}

	[ScriptLegacyMethod("ViewportToWorldPoint")]
	public Vector3 LegacyViewportToWorldPoint(Vector3 pos)
	{
		Viewport viewport = GDNode.GetViewport();
		Camera3D camera = viewport.GetCamera3D();
		if (camera == null || viewport == null)
			return Vector3.Zero;

		Vector2 size = viewport.GetVisibleRect().Size;
		Vector2 screenPos = new(pos.X * size.X, pos.Y * size.Y);
		Vector3 origin = camera.ProjectRayOrigin(screenPos);
		Vector3 direction = camera.ProjectRayNormal(screenPos);
		return (origin + direction * pos.Z);
	}

	[ScriptLegacyMethod("ViewportToScreenPoint")]
	public Vector3 LegacyViewportToScreenPoint(Vector3 pos)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		Vector2 size = Root.Environment.CurrentCamera!.ViewportToScreenPoint(new(pos.X, pos.Y));
		return new(size.X, size.Y, pos.Z);
	}


	[ScriptLegacyMethod("ScreenPointToRay")]
	public RayResult? LegacyScreenPointToRay(Vector2 pos, Instance[]? ignoreList = null, float maxDistance = 10000f)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		return Root.Environment.CurrentCamera!.ScreenPointToRay(pos, ignoreList, maxDistance);
	}

	[ScriptLegacyMethod("ScreenPointToRay")]
	public RayResult? LegacyScreenPointToRay(Vector3 pos, Instance[]? ignoreList = null, float maxDistance = 10000f)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		return Root.Environment.CurrentCamera!.ScreenPointToRay(new(pos.X, pos.Y), ignoreList, maxDistance);
	}

	[ScriptLegacyMethod("ViewportPointToRay")]
	public RayResult? LegacyViewportPointToRay(Vector3 pos, Instance[]? ignoreList = null, float maxDistance = 10000f)
	{
		if (Root.Environment.CurrentCamera == null)
		{
			throw new Exception("Camera is missing");
		}
		return Root.Environment.CurrentCamera!.ViewportPointToRay(new(pos.X, pos.Y), ignoreList, maxDistance);
	}


	[ScriptLegacyMethod("GetButton")]
	public bool LegacyGetButton(string buttonName)
	{
		if (_legacyKeydowns.TryGetValue(buttonName, out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetButtonDown")]
	public bool LegacyGetButtonDown(string buttonName)
	{
		if (_legacyFrameKeydowns.TryGetValue(buttonName, out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetButtonUp")]
	public bool LegacyGetButtonUp(string buttonName)
	{
		if (_legacyFrameKeydowns.TryGetValue(buttonName, out bool isDown))
		{
			return !isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetAxis")]
	public float LegacyGetAxis(string axisName)
	{
		return Mathf.Clamp(LegacyGetAxisRaw(axisName), -1f, 1f);
	}

	[ScriptLegacyMethod("GetAxisRaw")]
	public float LegacyGetAxisRaw(string axisName)
	{
		float value = 0f;
		Viewport viewport = GDNode.GetViewport();
		Vector2 size = viewport.GetVisibleRect().Size;

		switch (axisName)
		{
			case "Horizontal":
				{
					bool right = Input.IsActionPressed("rightward");
					bool left = Input.IsActionPressed("leftward");
					if (right) value += 1f;
					if (left) value -= 1f;
					break;
				}

			case "Vertical":
				{
					bool forward = Input.IsActionPressed("forward");
					bool backward = Input.IsActionPressed("backward");
					if (forward) value += 1f;
					if (backward) value -= 1f;
					break;
				}

			case "Mouse X":
				{
					value = Input.GetLastMouseVelocity().X / size.X;
					break;
				}

			case "Mouse Y":
				{
					value = -Input.GetLastMouseVelocity().Y / size.Y;
					break;
				}

			case "Mouse ScrollWheel":
				{
					value = _mouseScrollDelta;
					break;
				}

			default:
				GD.PushWarning($"GetAxis: Unknown axis '{axisName}'");
				break;
		}

		return value;
	}
	[ScriptLegacyMethod("GetKey")]
	public bool LegacyGetKey(LegacyKeyCode key)
	{
		if (_legacyKeydowns.TryGetValue(key.ToString(), out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetKeyDown")]
	public bool LegacyGetKeyDown(LegacyKeyCode key)
	{
		if (_legacyFrameKeydowns.TryGetValue(key.ToString(), out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetKeyUp")]
	public bool LegacyGetKeyUp(LegacyKeyCode key)
	{
		if (_legacyFrameKeydowns.TryGetValue(key.ToString(), out bool isDown))
		{
			return !isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetMouseButton")]
	public bool LegacyGetMouseButton(int button)
	{
		if (_mouseBtnDown.TryGetValue((MouseButton)button + 1, out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetMouseButtonDown")]
	public bool LegacyGetMouseButtonDown(int button)
	{
		if (_mouseFrameBtnDown.TryGetValue((MouseButton)button + 1, out bool isDown))
		{
			return isDown;
		}
		return false;
	}

	[ScriptLegacyMethod("GetMouseButtonUp")]
	public bool LegacyGetMouseButtonUp(int button)
	{
		if (_mouseFrameBtnDown.TryGetValue((MouseButton)button + 1, out bool isDown))
		{
			return !isDown;
		}
		return false;
	}
}
