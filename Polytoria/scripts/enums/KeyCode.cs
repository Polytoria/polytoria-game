// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Enums;

[ScriptEnum]
public enum KeyCodeEnum
{
	None = 0,
	/// <summary>
	/// <para>Keycodes with this bit applied are non-printable.</para>
	/// </summary>
	Special = 4194304,
	/// <summary>
	/// <para>Escape key.</para>
	/// </summary>
	Escape = 4194305,
	/// <summary>
	/// <para>Tab key.</para>
	/// </summary>
	Tab = 4194306,
	/// <summary>
	/// <para>Shift + Tab key.</para>
	/// </summary>
	Backtab = 4194307,
	/// <summary>
	/// <para>Backspace key.</para>
	/// </summary>
	Backspace = 4194308,
	/// <summary>
	/// <para>Return key (on the main keyboard).</para>
	/// </summary>
	Enter = 4194309,
	/// <summary>
	/// <para>Enter key on the numeric keypad.</para>
	/// </summary>
	KpEnter = 4194310,
	/// <summary>
	/// <para>Insert key.</para>
	/// </summary>
	Insert = 4194311,
	/// <summary>
	/// <para>Delete key.</para>
	/// </summary>
	Delete = 4194312,
	/// <summary>
	/// <para>Left arrow key.</para>
	/// </summary>
	Left = 4194319,
	/// <summary>
	/// <para>Up arrow key.</para>
	/// </summary>
	Up = 4194320,
	/// <summary>
	/// <para>Right arrow key.</para>
	/// </summary>
	Right = 4194321,
	/// <summary>
	/// <para>Down arrow key.</para>
	/// </summary>
	Down = 4194322,
	/// <summary>
	/// <para>Page Up key.</para>
	/// </summary>
	PageUp = 4194323,
	/// <summary>
	/// <para>Page Down key.</para>
	/// </summary>
	PageDown = 4194324,
	/// <summary>
	/// <para>Shift key.</para>
	/// </summary>
	Shift = 4194325,
	/// <summary>
	/// <para>Control key.</para>
	/// </summary>
	Ctrl = 4194326,
	/// <summary>
	/// <para>Meta key.</para>
	/// </summary>
	Meta = 4194327,
	/// <summary>
	/// <para>Alt key.</para>
	/// </summary>
	Alt = 4194328,
	/// <summary>
	/// <para>Caps Lock key.</para>
	/// </summary>
	CapsLock = 4194329,
	/// <summary>
	/// <para>Num Lock key.</para>
	/// </summary>
	NumLock = 4194330,
	/// <summary>
	/// <para>Scroll Lock key.</para>
	/// </summary>
	ScrollLock = 4194331,
	/// <summary>
	/// <para>F1 key.</para>
	/// </summary>
	F1 = 4194332,
	/// <summary>
	/// <para>F2 key.</para>
	/// </summary>
	F2 = 4194333,
	/// <summary>
	/// <para>F3 key.</para>
	/// </summary>
	F3 = 4194334,
	/// <summary>
	/// <para>F4 key.</para>
	/// </summary>
	F4 = 4194335,
	/// <summary>
	/// <para>F5 key.</para>
	/// </summary>
	F5 = 4194336,
	/// <summary>
	/// <para>F6 key.</para>
	/// </summary>
	F6 = 4194337,
	/// <summary>
	/// <para>F7 key.</para>
	/// </summary>
	F7 = 4194338,
	/// <summary>
	/// <para>F8 key.</para>
	/// </summary>
	F8 = 4194339,
	/// <summary>
	/// <para>F9 key.</para>
	/// </summary>
	F9 = 4194340,
	/// <summary>
	/// <para>F10 key.</para>
	/// </summary>
	F10 = 4194341,
	/// <summary>
	/// <para>F11 key.</para>
	/// </summary>
	F11 = 4194342,
	/// <summary>
	/// <para>F12 key.</para>
	/// </summary>
	F12 = 4194343,
	/// <summary>
	/// <para>Multiply (*) key on the numeric keypad.</para>
	/// </summary>
	KpMultiply = 4194433,
	/// <summary>
	/// <para>Divide (/) key on the numeric keypad.</para>
	/// </summary>
	KpDivide = 4194434,
	/// <summary>
	/// <para>Subtract (-) key on the numeric keypad.</para>
	/// </summary>
	KpSubtract = 4194435,
	/// <summary>
	/// <para>Period (.) key on the numeric keypad.</para>
	/// </summary>
	KpPeriod = 4194436,
	/// <summary>
	/// <para>Add (+) key on the numeric keypad.</para>
	/// </summary>
	KpAdd = 4194437,
	/// <summary>
	/// <para>Number 0 on the numeric keypad.</para>
	/// </summary>
	Kp0 = 4194438,
	/// <summary>
	/// <para>Number 1 on the numeric keypad.</para>
	/// </summary>
	Kp1 = 4194439,
	/// <summary>
	/// <para>Number 2 on the numeric keypad.</para>
	/// </summary>
	Kp2 = 4194440,
	/// <summary>
	/// <para>Number 3 on the numeric keypad.</para>
	/// </summary>
	Kp3 = 4194441,
	/// <summary>
	/// <para>Number 4 on the numeric keypad.</para>
	/// </summary>
	Kp4 = 4194442,
	/// <summary>
	/// <para>Number 5 on the numeric keypad.</para>
	/// </summary>
	Kp5 = 4194443,
	/// <summary>
	/// <para>Number 6 on the numeric keypad.</para>
	/// </summary>
	Kp6 = 4194444,
	/// <summary>
	/// <para>Number 7 on the numeric keypad.</para>
	/// </summary>
	Kp7 = 4194445,
	/// <summary>
	/// <para>Number 8 on the numeric keypad.</para>
	/// </summary>
	Kp8 = 4194446,
	/// <summary>
	/// <para>Number 9 on the numeric keypad.</para>
	/// </summary>
	Kp9 = 4194447,
	/// <summary>
	/// <para>Context menu key.</para>
	/// </summary>
	Menu = 4194370,
	/// <summary>
	/// <para>Hyper key. (On Linux/X11 only).</para>
	/// </summary>
	Hyper = 4194371,
	/// <summary>
	/// <para>Unknown key.</para>
	/// </summary>
	Unknown = 8388607,
	/// <summary>
	/// <para>Space key.</para>
	/// </summary>
	Space = 32,
	/// <summary>
	/// <para>Exclamation mark (<c>!</c>) key.</para>
	/// </summary>
	Exclam = 33,
	/// <summary>
	/// <para>Double quotation mark (<c>"</c>) key.</para>
	/// </summary>
	QuotedBl = 34,
	/// <summary>
	/// <para>Number sign or <i>hash</i> (<c>#</c>) key.</para>
	/// </summary>
	Numbersign = 35,
	/// <summary>
	/// <para>Dollar sign (<c>$</c>) key.</para>
	/// </summary>
	Dollar = 36,
	/// <summary>
	/// <para>Percent sign (<c>%</c>) key.</para>
	/// </summary>
	Percent = 37,
	/// <summary>
	/// <para>Ampersand (<c>&amp;</c>) key.</para>
	/// </summary>
	Ampersand = 38,
	/// <summary>
	/// <para>Apostrophe (<c>'</c>) key.</para>
	/// </summary>
	Apostrophe = 39,
	/// <summary>
	/// <para>Left parenthesis (<c>(</c>) key.</para>
	/// </summary>
	ParenLeft = 40,
	/// <summary>
	/// <para>Right parenthesis (<c>)</c>) key.</para>
	/// </summary>
	Parenright = 41,
	/// <summary>
	/// <para>Asterisk (<c>*</c>) key.</para>
	/// </summary>
	Asterisk = 42,
	/// <summary>
	/// <para>Plus (<c>+</c>) key.</para>
	/// </summary>
	Plus = 43,
	/// <summary>
	/// <para>Comma (<c>,</c>) key.</para>
	/// </summary>
	Comma = 44,
	/// <summary>
	/// <para>Minus (<c>-</c>) key.</para>
	/// </summary>
	Minus = 45,
	/// <summary>
	/// <para>Period (<c>.</c>) key.</para>
	/// </summary>
	Period = 46,
	/// <summary>
	/// <para>Slash (<c>/</c>) key.</para>
	/// </summary>
	Slash = 47,
	/// <summary>
	/// <para>Number 0 key.</para>
	/// </summary>
	Key0 = 48,
	/// <summary>
	/// <para>Number 1 key.</para>
	/// </summary>
	Key1 = 49,
	/// <summary>
	/// <para>Number 2 key.</para>
	/// </summary>
	Key2 = 50,
	/// <summary>
	/// <para>Number 3 key.</para>
	/// </summary>
	Key3 = 51,
	/// <summary>
	/// <para>Number 4 key.</para>
	/// </summary>
	Key4 = 52,
	/// <summary>
	/// <para>Number 5 key.</para>
	/// </summary>
	Key5 = 53,
	/// <summary>
	/// <para>Number 6 key.</para>
	/// </summary>
	Key6 = 54,
	/// <summary>
	/// <para>Number 7 key.</para>
	/// </summary>
	Key7 = 55,
	/// <summary>
	/// <para>Number 8 key.</para>
	/// </summary>
	Key8 = 56,
	/// <summary>
	/// <para>Number 9 key.</para>
	/// </summary>
	Key9 = 57,
	/// <summary>
	/// <para>Colon (<c>:</c>) key.</para>
	/// </summary>
	Colon = 58,
	/// <summary>
	/// <para>Semicolon (<c>;</c>) key.</para>
	/// </summary>
	Semicolon = 59,
	/// <summary>
	/// <para>Less-than sign (<c>&lt;</c>) key.</para>
	/// </summary>
	Less = 60,
	/// <summary>
	/// <para>Equal sign (<c>=</c>) key.</para>
	/// </summary>
	Equal = 61,
	/// <summary>
	/// <para>Greater-than sign (<c>&gt;</c>) key.</para>
	/// </summary>
	Greater = 62,
	/// <summary>
	/// <para>Question mark (<c>?</c>) key.</para>
	/// </summary>
	Question = 63,
	/// <summary>
	/// <para>At sign (<c>@</c>) key.</para>
	/// </summary>
	At = 64,
	/// <summary>
	/// <para>A key.</para>
	/// </summary>
	A = 65,
	/// <summary>
	/// <para>B key.</para>
	/// </summary>
	B = 66,
	/// <summary>
	/// <para>C key.</para>
	/// </summary>
	C = 67,
	/// <summary>
	/// <para>D key.</para>
	/// </summary>
	D = 68,
	/// <summary>
	/// <para>E key.</para>
	/// </summary>
	E = 69,
	/// <summary>
	/// <para>F key.</para>
	/// </summary>
	F = 70,
	/// <summary>
	/// <para>G key.</para>
	/// </summary>
	G = 71,
	/// <summary>
	/// <para>H key.</para>
	/// </summary>
	H = 72,
	/// <summary>
	/// <para>I key.</para>
	/// </summary>
	I = 73,
	/// <summary>
	/// <para>J key.</para>
	/// </summary>
	J = 74,
	/// <summary>
	/// <para>K key.</para>
	/// </summary>
	K = 75,
	/// <summary>
	/// <para>L key.</para>
	/// </summary>
	L = 76,
	/// <summary>
	/// <para>M key.</para>
	/// </summary>
	M = 77,
	/// <summary>
	/// <para>N key.</para>
	/// </summary>
	N = 78,
	/// <summary>
	/// <para>O key.</para>
	/// </summary>
	O = 79,
	/// <summary>
	/// <para>P key.</para>
	/// </summary>
	P = 80,
	/// <summary>
	/// <para>Q key.</para>
	/// </summary>
	Q = 81,
	/// <summary>
	/// <para>R key.</para>
	/// </summary>
	R = 82,
	/// <summary>
	/// <para>S key.</para>
	/// </summary>
	S = 83,
	/// <summary>
	/// <para>T key.</para>
	/// </summary>
	T = 84,
	/// <summary>
	/// <para>U key.</para>
	/// </summary>
	U = 85,
	/// <summary>
	/// <para>V key.</para>
	/// </summary>
	V = 86,
	/// <summary>
	/// <para>W key.</para>
	/// </summary>
	W = 87,
	/// <summary>
	/// <para>X key.</para>
	/// </summary>
	X = 88,
	/// <summary>
	/// <para>Y key.</para>
	/// </summary>
	Y = 89,
	/// <summary>
	/// <para>Z key.</para>
	/// </summary>
	Z = 90,
	/// <summary>
	/// <para>Left bracket (<c>[lb]</c>) key.</para>
	/// </summary>
	BracketLeft = 91,
	/// <summary>
	/// <para>Backslash (<c>\</c>) key.</para>
	/// </summary>
	Backslash = 92,
	/// <summary>
	/// <para>Right bracket (<c>[rb]</c>) key.</para>
	/// </summary>
	BracketRight = 93,
	/// <summary>
	/// <para>Caret (<c>^</c>) key.</para>
	/// </summary>
	Asciicircum = 94,
	/// <summary>
	/// <para>Underscore (<c>_</c>) key.</para>
	/// </summary>
	Underscore = 95,
	/// <summary>
	/// <para>Backtick (<c>`</c>) key.</para>
	/// </summary>
	QuoteLeft = 96,
	/// <summary>
	/// <para>Left brace (<c>{</c>) key.</para>
	/// </summary>
	BraceLeft = 123,
	/// <summary>
	/// <para>Vertical bar or <i>pipe</i> (<c>|</c>) key.</para>
	/// </summary>
	Bar = 124,
	/// <summary>
	/// <para>Right brace (<c>}</c>) key.</para>
	/// </summary>
	BraceRight = 125,
	/// <summary>
	/// <para>Tilde (<c>~</c>) key.</para>
	/// </summary>
	Asciitilde = 126,
	/// <summary>
	/// <para>Yen symbol (<c>¥</c>) key.</para>
	/// </summary>
	Yen = 165,
	/// <summary>
	/// <para>Section sign (<c>§</c>) key.</para>
	/// </summary>
	Section = 167,
	/// <summary>
	/// <para>Game controller SDL button A. Corresponds to the bottom action button: Sony Cross, Xbox A, Nintendo B.</para>
	/// </summary>
	GamepadA = 1000,
	/// <summary>
	/// <para>Game controller SDL button B. Corresponds to the right action button: Sony Circle, Xbox B, Nintendo A.</para>
	/// </summary>
	GamepadB = 1001,
	/// <summary>
	/// <para>Game controller SDL button X. Corresponds to the left action button: Sony Square, Xbox X, Nintendo Y.</para>
	/// </summary>
	GamepadX = 1002,
	/// <summary>
	/// <para>Game controller SDL button Y. Corresponds to the top action button: Sony Triangle, Xbox Y, Nintendo X.</para>
	/// </summary>
	GamepadY = 1003,
	/// <summary>
	/// <para>Game controller SDL back button. Corresponds to the Sony Select, Xbox Back, Nintendo - button.</para>
	/// </summary>
	GamepadBack = 1004,
	/// <summary>
	/// <para>Game controller SDL guide button. Corresponds to the Sony PS, Xbox Home button.</para>
	/// </summary>
	GamepadGuide = 1005,
	/// <summary>
	/// <para>Game controller SDL start button. Corresponds to the Sony Options, Xbox Menu, Nintendo + button.</para>
	/// </summary>
	GamepadStart = 1006,
	/// <summary>
	/// <para>Game controller SDL left stick button. Corresponds to the Sony L3, Xbox L/LS button.</para>
	/// </summary>
	GamepadLeftStick = 1007,
	/// <summary>
	/// <para>Game controller SDL right stick button. Corresponds to the Sony R3, Xbox R/RS button.</para>
	/// </summary>
	GamepadRightStick = 1008,
	/// <summary>
	/// <para>Game controller SDL left shoulder button. Corresponds to the Sony L1, Xbox LB button.</para>
	/// </summary>
	GamepadLeftShoulder = 1009,
	/// <summary>
	/// <para>Game controller SDL right shoulder button. Corresponds to the Sony R1, Xbox RB button.</para>
	/// </summary>
	GamepadRightShoulder = 1010,
	/// <summary>
	/// <para>Game controller D-pad up button.</para>
	/// </summary>
	GamepadDpadUp = 1011,
	/// <summary>
	/// <para>Game controller D-pad down button.</para>
	/// </summary>
	GamepadDpadDown = 1012,
	/// <summary>
	/// <para>Game controller D-pad left button.</para>
	/// </summary>
	GamepadDpadLeft = 1013,
	/// <summary>
	/// <para>Game controller D-pad right button.</para>
	/// </summary>
	GamepadDpadRight = 1014,
	/// <summary>
	/// <para>Game controller SDL paddle 1 button.</para>
	/// </summary>
	GamepadPaddle1 = 1016,
	/// <summary>
	/// <para>Game controller SDL paddle 2 button.</para>
	/// </summary>
	GamepadPaddle2 = 1017,
	/// <summary>
	/// <para>Game controller SDL paddle 3 button.</para>
	/// </summary>
	GamepadPaddle3 = 1018,
	/// <summary>
	/// <para>Game controller SDL paddle 4 button.</para>
	/// </summary>
	GamepadPaddle4 = 1019,
	/// <summary>
	/// <para>Game controller SDL touchpad button.</para>
	/// </summary>
	GamepadTouchpad = 1020,
	/// <summary>
	/// <para>Primary mouse button, usually assigned to the left button.</para>
	/// </summary>
	MouseLeft = 2001,
	/// <summary>
	/// <para>Secondary mouse button, usually assigned to the right button.</para>
	/// </summary>
	MouseRight = 2002,
	/// <summary>
	/// <para>Middle mouse button.</para>
	/// </summary>
	MouseMiddle = 2003,
	/// <summary>
	/// <para>Mouse wheel scrolling up.</para>
	/// </summary>
	MouseWheelUp = 2004,
	/// <summary>
	/// <para>Mouse wheel scrolling down.</para>
	/// </summary>
	MouseWheelDown = 2005,
	/// <summary>
	/// <para>Mouse wheel left button (only present on some mice).</para>
	/// </summary>
	MouseWheelLeft = 2006,
	/// <summary>
	/// <para>Mouse wheel right button (only present on some mice).</para>
	/// </summary>
	MouseWheelRight = 2007,
	/// <summary>
	/// <para>Extra mouse button 1. This is sometimes present, usually to the sides of the mouse.</para>
	/// </summary>
	MouseXbutton1 = 2008,
	/// <summary>
	/// <para>Extra mouse button 2. This is sometimes present, usually to the sides of the mouse.</para>
	/// </summary>
	MouseXbutton2 = 2009,
	/// <summary>
	/// <para>Game controller left joystick x-axis.</para>
	/// </summary>
	GamepadAxisLeftX = 3000,
	/// <summary>
	/// <para>Game controller left joystick y-axis.</para>
	/// </summary>
	GamepadAxisLeftY = 3001,
	/// <summary>
	/// <para>Game controller right joystick x-axis.</para>
	/// </summary>
	GamepadAxisRightX = 3002,
	/// <summary>
	/// <para>Game controller right joystick y-axis.</para>
	/// </summary>
	GamepadAxisRightY = 3003,
	/// <summary>
	/// <para>Game controller left trigger axis.</para>
	/// </summary>
	GamepadAxisTriggerLeft = 3004,
	/// <summary>
	/// <para>Game controller right trigger axis.</para>
	/// </summary>
	GamepadAxisTriggerRight = 3005,
}
