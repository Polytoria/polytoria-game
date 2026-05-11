// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
#if CREATOR
#endif
using System;

namespace Polytoria.Shared;

public sealed partial class FreeLook : Camera3D
{
	public FreeLook()
	{
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
	}

	public World Root = null!;

	public void Attach(World game)
	{
		Root = game;
	}

	private float _moveSpeed = 8f;
	private float _rotateSpeed = 0.005f;
	private float _interpolation = 0.01f;

	private bool _isMouseCaptured;
	private Vector2I _lastMousePosition;

	private Vector2 _currentRotation = Vector2.Zero;
	private Vector3 _currentMovement = Vector3.Zero;

	public override void _Process(double delta)
	{
#if CREATOR
		if (Root != null && Root.CreatorContext != null)
		{
			if (!Root.CreatorContext.IsViewportFocused) { return; }
		}
#endif
		if (Input.IsKeyPressed(Key.Ctrl)) return;
		Vector2 horizontalInput = Input.GetVector("leftward", "rightward", "forward", "backward");
		float verticalInput = Input.GetAxis("downward", "upward");

		_currentMovement.X = horizontalInput.X;
		_currentMovement.Y = verticalInput;
		_currentMovement.Z = horizontalInput.Y;

		if (_currentMovement == Vector3.Zero && _currentRotation == Vector2.Zero)
		{
			return;
		}

		Transform3D temp = Transform;

		if (_currentRotation.X != 0)
		{
			temp.Basis = new Basis(Vector3.Up, _currentRotation.X * _rotateSpeed) * temp.Basis;
			_currentRotation.X = 0;
		}

		if (_currentRotation.Y != 0)
		{
			temp.Basis *= new Basis(Vector3.Right, _currentRotation.Y * _rotateSpeed);
			_currentRotation.Y = 0;
		}

		if (_currentMovement != Vector3.Zero)
		{
			temp.Origin += temp.Basis * (_currentMovement * _moveSpeed * (float)delta);
		}

		Transform = temp;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.Right)
			{
				if (button.Pressed)
				{
					_lastMousePosition = (Vector2I)GetViewport().GetMousePosition();
					Input.MouseMode = Input.MouseModeEnum.Captured;
					_isMouseCaptured = true;
				}
				else
				{
					_isMouseCaptured = false;
					Input.MouseMode = Input.MouseModeEnum.Visible;
					Vector2 globalMousePos = GetViewport().GetScreenTransform().Origin + _lastMousePosition;
					Input.WarpMouse(globalMousePos);
#if GODOT_WINDOWS
					Input.WarpMouse(globalMousePos); // Workaround for godotengine/godot#119205
#endif

					_currentMovement = Vector3.Zero;
					_currentRotation = Vector2.Zero;
				}
			}
			else if (_isMouseCaptured && button.Pressed)
			{
				if (button.ButtonIndex == MouseButton.WheelUp)
				{
					_moveSpeed = Mathf.Clamp(MathF.Round(_moveSpeed * 2, 1), 2, 1024);
					PT.Print("Camera Speed + ", _moveSpeed);
				}
				else if (button.ButtonIndex == MouseButton.WheelDown)
				{
					_moveSpeed = Mathf.Clamp(MathF.Round(_moveSpeed / 2), 2, 1024);
					PT.Print("Camera Speed - ", _moveSpeed);
				}
			}
		}
		else if (_isMouseCaptured)
		{
			if (@event is InputEventMouseMotion motion)
			{
				_currentRotation = -motion.ScreenRelative;
			}
		}
	}
}
