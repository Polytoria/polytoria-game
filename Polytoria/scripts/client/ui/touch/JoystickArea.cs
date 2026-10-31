// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Touch;

public partial class JoystickArea : InputFallbackBase
{
	private const string MoveAction = "forward";

	[Export] public float MaxThumbstickDistance = 80f;
	[Export] public float Deadzone = 8f;
	[Export(PropertyHint.Range, "0.1,1,0.01")] public float FullWalkAt = 0.55f;
	[Export] public float SprintStartDistance = 1.6f;
	[Export] public float SprintStopDistance = 1.35f;

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

		Vector2 thumb = (_endPos - _startPos).LimitLength(MaxThumbstickDistance);
		float travel = GetTravel(thumb);

		_line.ClearPoints();
		_line.AddPoint(_startPos);
		_line.AddPoint(_startPos + thumb);

		Vector2 axis = ToStickAxis(thumb.Normalized(), Mathf.Min(travel / FullWalkAt, 1f));

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

		float pushed = (_endPos - _startPos).Length() / MaxThumbstickDistance;
		SetSprint(_sprinting ? pushed >= SprintStopDistance : pushed >= SprintStartDistance);
	}

	private float GetTravel(Vector2 thumb)
	{
		float length = thumb.Length();
		if (length < Deadzone)
		{
			return 0f;
		}

		return (length - Deadzone) / (MaxThumbstickDistance - Deadzone);
	}

	private static Vector2 ToStickAxis(Vector2 direction, float speed)
	{
		if (speed <= 0f)
		{
			return Vector2.Zero;
		}

		float deadzone = InputMap.ActionGetDeadzone(MoveAction);
		return new(CompensateDeadzone(direction.X * speed, deadzone), CompensateDeadzone(direction.Y * speed, deadzone));
	}

	private static float CompensateDeadzone(float value, float deadzone)
	{
		if (Mathf.IsZeroApprox(value))
		{
			return 0f;
		}

		return Mathf.Sign(value) * (deadzone + (1f - deadzone) * Mathf.Abs(value));
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
