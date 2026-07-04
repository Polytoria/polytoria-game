using Godot;

namespace Polytoria.Client.UI;

public partial class TouchScrollContainer : ScrollContainer
{
	[Export] public bool TouchDragScroll = true;
	[Export] public float TouchScrollMultiplier = 1.0f;
	[Export] public float TouchDragDeadzone = 8.0f;

	private int? _activeTouchIndex;
	private Vector2 _touchStartPosition;
	private Vector2 _drag;
	private bool _isScrolling;

	public override void _Input(InputEvent @event)
	{
		if (!TouchDragScroll || !Visible || !IsInsideTree())
			return;

		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				if (_activeTouchIndex != null)
					return;

				if (!GetGlobalRect().HasPoint(touch.Position))
					return;

				_activeTouchIndex = touch.Index;
				_touchStartPosition = touch.Position;
				_drag = Vector2.Zero;
				_isScrolling = false;

				return;
			}

			if (_activeTouchIndex == touch.Index)
			{
				if (_isScrolling)
				{
					GetViewport().SetInputAsHandled();
				}

				_activeTouchIndex = null;
				_isScrolling = false;
				_drag = Vector2.Zero;
			}

			return;
		}

		if (@event is not InputEventScreenDrag drag)
			return;

		if (_activeTouchIndex != drag.Index)
			return;

		if (!GetGlobalRect().HasPoint(_touchStartPosition))
			return;

		_drag += drag.Relative;

		if (!_isScrolling)
		{
			if (Mathf.Abs(_drag.Y) > Mathf.Abs(_drag.X) && Mathf.Abs(_drag.Y) >= TouchDragDeadzone)
			{
				_isScrolling = true;
			}
		}

		VScrollBar bar = GetVScrollBar();
		float maxScroll = (float)Mathf.Max(0, bar.MaxValue - bar.Page);
		ScrollVertical = Mathf.RoundToInt(Mathf.Clamp(ScrollVertical - drag.Relative.Y * TouchScrollMultiplier, 0, maxScroll));

		GetViewport().SetInputAsHandled();
	}
}
