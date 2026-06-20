// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Touch;

public partial class JoystickArea : InputFallbackBase
{
	[Export] public float MaxThumbstickDistance = 220f;
	[Export] public float Deadzone = 10f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float SprintThreshold = 0.9f;

	private bool _dragging = false;
	private bool _sprinting = false;

	private Vector2 _startPos;
	private Vector2 _endPos;
	private Line2D _line = null!;

	private int? _activeTouchIndex = null;

	public override void _Ready()
	{
		_line = GetNode<Line2D>("Line");
	}

	public override void _Process(double delta)
	{
		if (!_dragging) { return; }

		Vector2 axis = GetThumbstickAxis();

		_line.ClearPoints();
		_line.AddPoint(_startPos);
		_line.AddPoint(_startPos + axis * MaxThumbstickDistance);

		InputEventJoypadMotion leftX = new()
		{
			Axis = JoyAxis.LeftX,
			AxisValue = axis.X
		};

		InputEventJoypadMotion leftY = new()
		{
			Axis = JoyAxis.LeftY,
			AxisValue = axis.Y
		};
		leftX.SetMeta("emulated", 1);
		leftY.SetMeta("emulated", 1);

		Input.ParseInputEvent(leftX);
		Input.ParseInputEvent(leftY);

		bool shouldSprint = axis.Length() >= SprintThreshold;
		SetSprint(shouldSprint);
	}

	private Vector2 GetThumbstickAxis()
	{
		Vector2 delta = _endPos - _startPos;

		if (delta.Length() < Deadzone)
		{
			return Vector2.Zero;
		}

		Vector2 clamped = delta.LimitLength(MaxThumbstickDistance);
		return clamped / MaxThumbstickDistance;
	}

	private static void SendInputEnd()
	{
		InputEventJoypadMotion leftX = new()
		{
			Axis = JoyAxis.LeftX,
			AxisValue = 0
		};

		InputEventJoypadMotion leftY = new()
		{
			Axis = JoyAxis.LeftY,
			AxisValue = 0
		};

		Input.ParseInputEvent(leftX);
		Input.ParseInputEvent(leftY);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed && _activeTouchIndex == null)
			{
				_activeTouchIndex = touch.Index;
				_startPos = touch.Position;
				_endPos = _startPos;
				_dragging = true;
				_line.Visible = true;
				AcceptEvent();
			}
			else if (!touch.Pressed && _activeTouchIndex == touch.Index)
			{
				_activeTouchIndex = null;
				_dragging = false;
				_line.Visible = false;
				SetSprint(false);
				SendInputEnd();
				AcceptEvent();
			}
		}
		else if (@event is InputEventScreenDrag drag && _dragging && drag.Index == _activeTouchIndex)
		{
			_endPos = drag.Position;
			AcceptEvent();
		}
		base._GuiInput(@event);
	}

	private void SetSprint(bool sprint)
	{
		if (_sprinting == sprint)
		{
			return;
		}

		_sprinting = sprint;

		InputEventAction sprintEvent = new()
		{
			Action = "sprint",
			Pressed = _sprinting
		};

		sprintEvent.SetMeta("emulated", 1);
		Input.ParseInputEvent(sprintEvent);
	}
}
