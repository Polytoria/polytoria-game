// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Utils;

namespace Polytoria.Client;

public partial class GamepadFocusbox : Control
{
	public float FocusLerpSpeed = 20;
	public Vector2 OutlineOffset = new(16, 16);
	private bool _isGamepadActive = false;
	private bool _gameFocus = false;
	private Vector2 _targetPos = Vector2.Zero;
	private Vector2 _targetSize = Vector2.Zero;

	public override void _Ready()
	{
		Visible = false;
		SetProcess(true);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventJoypadButton || @event is InputEventJoypadMotion joyMotion && Mathf.Abs(joyMotion.AxisValue) > 0.2f)
		{
			if (@event.HasMeta("emulated")) return;
			_isGamepadActive = true;
		}
		else if (@event is InputEventKey || @event is InputEventMouseButton || @event is InputEventMouseMotion)
		{
			_isGamepadActive = false;
			Visible = false;
		}
	}

	public override void _Process(double delta)
	{
		if (!_isGamepadActive) { return; }

		Control? focusOwner = GetViewport().GuiGetFocusOwner();
		if (focusOwner != null)
		{
			if (focusOwner.Name == "InputFallback")
			{
				focusOwner = null;
			}
		}

		if (focusOwner != null)
		{
			_targetPos = focusOwner.GlobalPosition;
			_targetSize = focusOwner.Size;

			float weight = MathUtils.ExpDecay((float)delta, FocusLerpSpeed);
			GlobalPosition = GlobalPosition.Lerp(_targetPos - OutlineOffset / 2, weight);
			Size = Size.Lerp(_targetSize + OutlineOffset, weight);

			Visible = true;
		}
		else
		{
			Visible = false;
		}
	}
}
