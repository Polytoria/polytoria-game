// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
namespace Polytoria.Client.UI;

public partial class DevConsoleWindow : Control
{
	[Export] private Button _closeButton = null!;
	[Export] private Control _dragZone = null!;
	[Export] private Control _resizeZone = null!;
	[Export] private Vector2 _minSize = new(400, 100);

	private bool _isDragging = false;
	private Vector2 _dragOffset = Vector2.Zero;

	private bool _isResizing = false;
	private Vector2 _resizeStartSize = Vector2.Zero;
	private Vector2 _resizeStartMousePos = Vector2.Zero;

	public override void _EnterTree()
	{
		_closeButton.Pressed += OnCloseRequested;
		_dragZone.GuiInput += OnDragZoneInput;
		_resizeZone.GuiInput += OnResizeZoneInput;
		base._EnterTree();
	}

	private void OnDragZoneInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					// Start dragging
					_isDragging = true;
					_dragOffset = mouseButton.Position;
				}
				else
				{
					// Stop dragging
					_isDragging = false;
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			if (_isDragging)
			{
				// Update window position
				Position += mouseMotion.Position - _dragOffset;
			}
		}
	}

	private void OnResizeZoneInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					// Start resizing
					_isResizing = true;
					_resizeStartSize = Size;
					_resizeStartMousePos = GetGlobalMousePosition();
				}
				else
				{
					// Stop resizing
					_isResizing = false;
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			if (_isResizing)
			{
				// Calculate the mouse delta from the resize start position
				Vector2 currentMousePos = GetGlobalMousePosition();
				Vector2 mouseDelta = currentMousePos - _resizeStartMousePos;

				// Update window size
				Vector2 newSize = _resizeStartSize + mouseDelta;

				// Apply minimum size constraints
				newSize.X = Mathf.Max(newSize.X, _minSize.X);
				newSize.Y = Mathf.Max(newSize.Y, _minSize.Y);

				Size = newSize;
			}
		}
	}

	public override void _ExitTree()
	{
		_closeButton.Pressed -= OnCloseRequested;
		_dragZone.GuiInput -= OnDragZoneInput;
		_resizeZone.GuiInput -= OnResizeZoneInput;
		base._ExitTree();
	}

	public void Toggle()
	{
		Visible = !Visible;
	}

	private void OnCloseRequested()
	{
		Visible = false;
	}
}
