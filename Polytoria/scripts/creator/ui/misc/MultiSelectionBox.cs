// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Interfaces;

namespace Polytoria.Creator;

public partial class MultiSelectionBox : Control
{
	private const float BoxSizeThreshold = 5;
	private bool _dragging;
	private Vector2 _dragStart;

	[Export]
	public WorldContainerOverlay Overlay = null!;

	[Export]
	private Panel _panel = null!;

	[Export]
	private Control _pivotControl = null!;

	[Export]
	private int _selectSensitivity = 50;

	private Tween? _tween;

	private void CalculateBox(Vector2 endPosition)
	{
		Vector2 topLeft = _dragStart - _pivotControl.GlobalPosition;
		Vector2 bottomRight = endPosition - _pivotControl.GlobalPosition;

		if (topLeft.X > bottomRight.X)
		{
			(bottomRight.X, topLeft.X) = (topLeft.X, bottomRight.X);
		}
		if (topLeft.Y > bottomRight.Y)
		{
			(bottomRight.Y, topLeft.Y) = (topLeft.Y, bottomRight.Y);
		}

		Rect2 box = new(topLeft, bottomRight - topLeft);
		if (box.Size.Length() < BoxSizeThreshold)
		{
			return;
		}
		Instance[] allObjects = Overlay.World.Environment.GetDescendants();

		Overlay.World.CreatorContext.Selections.DeselectAll();

		bool altPressed = Input.IsKeyPressed(Key.Alt);

		foreach (Instance item in allObjects)
		{
			if (item is Dynamic dyn)
			{
				var camera = Overlay.World.CreatorContext.Freelook.Camera3D;
				var globalPos = dyn.GetGlobalPosition();

				// Check if position is within frustum
				if (!camera.IsPositionInFrustum(globalPos))
					continue;

				if (box.HasPoint(camera.UnprojectPosition(globalPos)))
				{
					Instance? top = dyn;
					if (!altPressed)
					{
						// Get model root if ALT is not pressed
						top = Gizmos.GetModelRoot(dyn);
					}
					if (top == null) continue;
					if (top is Dynamic pd && pd.Locked) continue;

					// Don't select model if alt pressed
					if (altPressed && (top is IGroup)) continue;
					Overlay.World.CreatorContext.Selections.Select(top);
				}
			}
		}

		// Return focus to container
		Overlay.Container.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		Gizmos gizmos = Overlay.World.CreatorContext.Gizmos;
		CreatorSelections selections = Overlay.World.CreatorContext.Selections;
		Vector2 mousePosition = GetViewport().GetMousePosition();

		if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (mouseEvent.Pressed)
			{
				if (!_dragging && !gizmos.HoveringGizmos && selections.SelectedInstances.Count == 0)
				{
					_tween?.Stop();

					_dragging = true;
					_dragStart = mousePosition;
					_panel.Size = Vector2.Zero;
					_panel.Visible = true;
					_panel.Modulate = new Color(1, 1, 1, 1);
				}
			}
			else if (_dragging)
			{
				_dragging = false;
				if ((_dragStart - mousePosition).Length() > _selectSensitivity)
				{
					_tween = GetTree().CreateTween();
					_tween.TweenProperty(_panel, "modulate", new Color(1, 1, 1, 0), 0.15f);
					_tween.TweenCallback(Callable.From(() =>
					{
						_panel.Visible = false;
						_panel.Size = Vector2.Zero;
					}));

					CalculateBox(mousePosition);
				}
				else
				{
					_panel.Visible = false;
					_panel.Size = Vector2.Zero;
				}
			}
		}

		if (@event is InputEventMouseMotion && _dragging)
		{
			Vector2 sizeProc = mousePosition - _dragStart;
			Vector2 pos = _dragStart;

			if (sizeProc.X < 0)
			{
				pos += new Vector2(sizeProc.X, 0);
			}

			if (sizeProc.Y < 0)
			{
				pos += new Vector2(0, sizeProc.Y);
			}

			_panel.GlobalPosition = pos;
			_panel.Size = new Vector2(Mathf.Abs(sizeProc.X), Mathf.Abs(sizeProc.Y));
		}
	}
}
