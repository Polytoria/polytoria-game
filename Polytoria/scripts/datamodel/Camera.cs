// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Godot.Collections;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Scripting;
using Polytoria.Shared.Misc;
using Polytoria.Utils;
using System;
using static Polytoria.Datamodel.Environment;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Camera : Dynamic
{
	public const float ClipSafeMargin = 2.0f;
	public const float DefaultZoomDistance = 10.0f;
	public const float DefaultScrollSensitivity = 15.0f;
	private CameraModeEnum _mode;
	private float _fov;
	private bool _clipThroughWalls;
	private float _minDistance;
	private float _maxDistance;
	private float _scrollSensitivity;
	private bool _orthographic;
	private float _lerpSpeed;
	private float _scrollLerpSpeed;
	private float _orthographicSize;
	private Vector3 _positionOffset;
	private Vector3 _rotationOffset;
	private bool _isFirstPerson;
	private float _sensitivityMultipler = 1f;
	private bool _canLock = true;
	private float _xSpeed = 120.0f;
	private float _ySpeed = 120.0f;
	private bool _followLerp = false;
	private bool _ctrlLocked = false;
	private bool _alwaysLocked = false;

	private float _moveSpeed = 8f;
	private readonly float _rotateSpeed = 0.005f;
	private Dynamic? _target = null!;

	private bool _isMouseCaptured;
	private Vector2I _lastMousePosition;

	private Vector2 _currentRotation = Vector2.Zero;
	private Vector3 _currentMovement = Vector3.Zero;

	private Vector3 _targetRotation = new(0, 180, 0);
	private float _targetZoom = DefaultZoomDistance;
	private float _currentZoom = DefaultZoomDistance;
	private float _distance = DefaultZoomDistance;
	private bool _turning = false;
	private Vector2 _turnStartPos;
	private Node3D _turnX = null!;
	private Node3D _turnY = null!;
	private Node3D _turnY2 = null!;
	private InputHelper _inputHelper = null!;

	internal Camera3D Camera3D = null!;

	[Editable, ScriptProperty, DefaultValue(CameraModeEnum.Follow)]
	public CameraModeEnum Mode
	{
		get => _mode;
		set
		{
			_mode = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(75)]
	public float FOV
	{
		get => _fov;
		set
		{
			_fov = value;
			Camera3D.Fov = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool ClipThroughWalls
	{
		get => _clipThroughWalls;
		set
		{
			_clipThroughWalls = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float MinDistance
	{
		get => _minDistance;
		set
		{
			_minDistance = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(20f)]
	public float MaxDistance
	{
		get => _maxDistance;
		set
		{
			_maxDistance = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float Distance
	{
		get => _distance;
		set
		{
			_targetZoom = value;
			LimitZoomDistance();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(DefaultScrollSensitivity)]
	public float ScrollSensitivity
	{
		get => _scrollSensitivity;
		set
		{
			_scrollSensitivity = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Orthographic
	{
		get => _orthographic;
		set
		{
			_orthographic = value;
			Camera3D.Projection = value ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool FollowLerp
	{
		get => _followLerp;
		set { _followLerp = value; OnPropertyChanged(); }
	}

	[Editable, ScriptProperty, DefaultValue(15f)]
	public float LerpSpeed
	{
		get => _lerpSpeed;
		set
		{
			_lerpSpeed = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float OrthographicSize
	{
		get => _orthographicSize;
		set
		{
			_orthographicSize = value;
			Camera3D.Size = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 PositionOffset
	{
		get => _positionOffset;
		set
		{
			_positionOffset = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector3 RotationOffset
	{
		get => _rotationOffset;
		set
		{
			_rotationOffset = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public bool IsFirstPerson
	{
		get => _isFirstPerson;
		private set
		{
			_isFirstPerson = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool CanLock
	{
		get => _canLock;
		set
		{
			_canLock = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float SensitivityMultiplier
	{
		get => _sensitivityMultipler;
		set
		{
			_sensitivityMultipler = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public float Sensitivity
	{
		get => ClientSettingsService.Instance.Get<float>(ClientSettingKeys.General.CameraSensitivity) * _sensitivityMultipler;
	}

	[ScriptProperty]
	public float HorizontalSpeed
	{
		get => _xSpeed; set => _xSpeed = value;
	}

	[ScriptProperty]
	public float VerticalSpeed
	{
		get => _ySpeed; set => _ySpeed = value;
	}

	[Editable, ScriptProperty, DefaultValue(15f)]
	public float ScrollLerpSpeed
	{
		get => _scrollLerpSpeed;
		set { _scrollLerpSpeed = value; OnPropertyChanged(); }
	}

	[ScriptProperty]
	public bool CtrlLocked
	{
		get => _ctrlLocked;
		set
		{
			_ctrlLocked = value;
			if (_ctrlLocked)
			{
				StartTurning();
			}
			else
			{
				if (!AlwaysLocked)
				{
					StopTurning();
				}
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public bool AlwaysLocked
	{
		get => _alwaysLocked;
		set
		{
			_alwaysLocked = value;
			if (_alwaysLocked)
			{
				CtrlLocked = true;
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public Dynamic? Target
	{
		get
		{
			if (_target != null && _target.IsDeleted)
			{
				_target = null!;
			}
			return _target;
		}
		set => _target = value;
	}

	public enum CameraModeEnum
	{
		Follow = 0,
		Free = 1,
		Scripted = 2,
	}

	public enum LegacyCameraModeEnum
	{
		FollowPlayer = 0,
		Free = 1,
		Scripted = 2,
	}

	[ScriptProperty]
	public PTSignal FirstPersonEntered { get; private set; } = new();

	[ScriptProperty]
	public PTSignal FirstPersonExited { get; private set; } = new();

	/// <summary>
	/// Should camera be updating itself or not.
	/// </summary>
	internal bool UpdateCameraSelf = true;

	public override void EnterTree()
	{
		// keep the current camera current
		EnforceCurrentCam();

		base.EnterTree();
	}

	public override void ExitTree()
	{
		// keep the current camera current
		EnforceCurrentCam();

		base.ExitTree();
	}

	private void EnforceCurrentCam()
	{
		// idk godot
		Callable.From(() =>
		{
			Root.Environment?.EnforceCamera();
		}).CallDeferred();
	}


	public override void Init()
	{
		base.Init();

		GDNode.AddChild(_inputHelper = new(), @internal: Node.InternalMode.Back);
		_inputHelper.GodotUnhandledInputEvent += OnInput;

		GDNode3D.AddChild(Camera3D = new());
		// Flip the camera
		Camera3D.RotationDegrees = new(0, 180, 0);

		_turnX = new Node3D();
		_turnY = new Node3D();
		_turnY2 = new Node3D();
		_turnX.AddChild(_turnY);
		_turnX.AddChild(_turnY2);
		GDNode3D.GetParent().AddChild(_turnX);

		_turnY.RotationDegrees = new(0, 180, 0);

		FOV = 75;

		if (Root.Input != null)
		{
			Root.Input.GameFocused.Connect(OnGameFocused);
			Root.Input.GameUnfocused.Connect(OnGameUnfocused);
		}

		SetProcess(true);
	}

	public override void PreDelete()
	{
		_inputHelper.GodotUnhandledInputEvent -= OnInput;
		_inputHelper.QueueFree();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		if (UpdateCameraSelf)
		{
			CameraProcess(delta);
		}
		base.Process(delta);
	}

	internal void CameraProcess(double delta)
	{
		if (Root.Environment.CurrentCamera != this) return;
		if (Mode == CameraModeEnum.Follow && Target != null)
		{
			if (Root.Input.IsGameFocused)
			{
				// Handle Controller Right stick input
				float xAxis = Input.GetAxis("cam_rightward", "cam_leftward");
				float yAxis = Input.GetAxis("cam_downward", "cam_upward");

				_targetRotation += new Vector3(yAxis, xAxis, 0) * Sensitivity;
				LimitRotation();
			}

			Vector3 computedPosition = Target.Position.Flip() + PositionOffset.Flip();
			Vector3 computedRotation = _targetRotation + RotationOffset;

			_turnX.GlobalPosition = computedPosition;
			_turnX.RotationDegrees = computedRotation;

			LimitZoomDistance();

			if ((IsFirstPerson || AlwaysLocked) && Root.Input.IsGameFocused)
			{
				// Force mouse captured
				Input.MouseMode = Input.MouseModeEnum.Captured;
				Root.Input.OverrideMousePosTo = GDNode.GetViewport().GetVisibleRect().GetCenter().Flip();
			}

			if (_targetZoom <= 0)
			{
				if (!IsFirstPerson)
				{
					EnterFirstPerson();
				}
			}
			else
			{
				if (IsFirstPerson)
				{
					ExitFirstPerson();
				}
			}

			_currentZoom = (float)Mathf.Lerp(_currentZoom, _targetZoom, delta * ScrollLerpSpeed);
			float finalizedZoom = _currentZoom;

			_turnY2.Position = new Vector3(0, 0, _currentZoom);

			if (!ClipThroughWalls)
			{
				Vector3 desiredCamPos = _turnY2.GlobalPosition;
				Vector3 origin = computedPosition;

				PhysicsDirectSpaceState3D spaceState = Camera3D.GetWorld3D().DirectSpaceState;
				PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, desiredCamPos);
				query.HitFromInside = false;

				// Fliter only clipping layers
				query.CollisionMask = 1 << 5;

				Dictionary? result = spaceState.IntersectRay(query);

				if (result.Count > 0)
				{
					Vector3 hitPoint = (Vector3)result["position"];

					float hitDist = origin.DistanceTo(hitPoint) - ClipSafeMargin;
					finalizedZoom = Mathf.Min(_distance, hitDist);

					// Prevent zooming after zero
					if (finalizedZoom < 0)
					{
						finalizedZoom = 0;
					}
				}
			}

			_distance = finalizedZoom;

			_turnY.Position = new Vector3(0, 0, _distance);

			Vector3 posSetto = _turnY.GlobalPosition;

			// Apply position/rotation
			if (FollowLerp)
			{
				GDNode3D.GlobalPosition = GDNode3D.GlobalPosition.Lerp(posSetto, (float)(delta * LerpSpeed));
			}
			else
			{
				GDNode3D.GlobalPosition = posSetto;
			}

			GDNode3D.GlobalBasis = _turnY.GlobalBasis;
		}
		else if (Mode == CameraModeEnum.Free)
		{
			if (Input.IsKeyPressed(Key.Ctrl)) return;
			if (!Root.Input.IsGameFocused) return;
			Vector2 horizontalInput = Input.GetVector("leftward", "rightward", "forward", "backward");
			float verticalInput = Input.GetAxis("downward", "upward");

			float speedMultipler = 1;

			if (Input.IsActionPressed("freelook_shift"))
			{
				speedMultipler = 1.75f;
			}

			_currentMovement.X = horizontalInput.X;
			_currentMovement.Y = -verticalInput;
			_currentMovement.Z = horizontalInput.Y;

			if (_currentMovement == Vector3.Zero && _currentRotation == Vector2.Zero)
			{
				return;
			}

			Transform3D temp = GetGlobalTransform();

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
				temp.Origin -= temp.Basis * (_currentMovement * (_moveSpeed * speedMultipler) * (float)delta);
			}

			SetGlobalTransform(temp);
		}
	}

	private void LimitZoomDistance()
	{
		// Limit zoom distance
		if (_targetZoom < MinDistance)
		{
			_targetZoom = MinDistance;
		}
		if (_targetZoom > MaxDistance)
		{
			_targetZoom = MaxDistance;
		}
	}
	private void EnterFirstPerson()
	{
		if (Mode != CameraModeEnum.Follow) return;
		IsFirstPerson = true;
		_targetZoom = 0;
		StartTurning();
		FirstPersonEntered?.Invoke();
	}

	private void ExitFirstPerson(bool resetZoom = false)
	{
		if (Mode != CameraModeEnum.Follow) return;
		IsFirstPerson = false;
		if (resetZoom)
		{
			_targetZoom = DefaultZoomDistance;
		}
		if (!CtrlLocked)
		{
			StopTurning();
		}
		FirstPersonExited?.Invoke();
	}

	private void OnGameFocused()
	{
		if (IsFirstPerson || AlwaysLocked)
		{
			if (AlwaysLocked)
			{
				CtrlLocked = true;
			}
			else
			{
				StartTurning();
			}
		}
	}

	private void OnGameUnfocused()
	{
		if (CtrlLocked)
		{
			CtrlLocked = false;
			StopTurning();
		}
		if (_turning)
		{
			StopTurning();
		}
	}

	private void ToggleFirstPerson()
	{
		if (IsFirstPerson)
		{
			ExitFirstPerson(true);
		}
		else
		{
			EnterFirstPerson();
		}
	}

	private void StartTurning()
	{
		if (Mode != CameraModeEnum.Follow) return;
		_turning = true;
		_turnStartPos = GDNode.GetViewport().GetMousePosition();
		Root.Input.OverrideMousePosTo = Root.Input.MousePosition;
		Root.Input.OverrideMousePos = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void StopTurning()
	{
		if (Mode != CameraModeEnum.Follow) return;
		_turning = false;
		if (!Root.Input.CursorLocked)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			Root.Input.OverrideMousePos = false;
			GDNode.GetViewport().WarpMouse(_turnStartPos);
#if GODOT_WINDOWS
			GDNode.GetViewport().WarpMouse(_turnStartPos); // Workaround for godotengine/godot#119205
#endif
		}
	}

	private void OnInput(InputEvent @event)
	{
		if (Root.Environment.CurrentCamera != this) return;

		if (!Root.Input.IsGameFocused) return;
		if (@event is InputEventMouseButton btnEvent2)
		{
			if (btnEvent2.Pressed)
			{
				// Handle scroll events
				switch (btnEvent2.ButtonIndex)
				{
					case MouseButton.WheelUp:
						_targetZoom = _distance - (ScrollSensitivity / 5);
						break;

					case MouseButton.WheelDown:
						_targetZoom = _distance + (ScrollSensitivity / 5);
						break;
				}
			}
		}

		if (Mode == CameraModeEnum.Scripted) return;

		if (Mode == CameraModeEnum.Free)
		{
			HandleFreeCam(@event);
			return;
		}

		if (@event is InputEventMouseButton btnEvent)
		{
			if (btnEvent.ButtonIndex == MouseButton.Right && !(IsFirstPerson || CtrlLocked))
			{
				if (AlwaysLocked) return;
				if (btnEvent.Pressed)
				{
					StartTurning();
				}
				else
				{
					StopTurning();
				}
			}
		}

		if (@event is InputEventMouseMotion mouseEvent)
		{
			if (Root.Input.IsTouchscreen) return;
			if (_turning)
			{
				_targetRotation += new Vector3(mouseEvent.Relative.Y / -5 * VerticalSpeed * 0.02f, mouseEvent.Relative.X / -5 * HorizontalSpeed * 0.02f, 0) * Sensitivity;

				LimitRotation();
			}
		}

		// Toggle first person
		if (@event.IsActionPressed("cam_toggle_firstperson"))
		{
			ToggleFirstPerson();
		}

		// Ctrl Lock Toggle
		if (CanLock && @event.IsActionPressed("ctrllock") && !IsFirstPerson && ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.General.CtrlLock))
		{
			if (AlwaysLocked) return;
			CtrlLocked = !CtrlLocked;
		}
	}

	private void HandleFreeCam(InputEvent @event)
	{
		if (@event is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.Right)
			{
				if (button.Pressed)
				{
					_lastMousePosition = (Vector2I)GDNode.GetViewport().GetMousePosition();
					Input.MouseMode = Input.MouseModeEnum.Captured;
					_isMouseCaptured = true;
				}
				else
				{
					_isMouseCaptured = false;
					Input.MouseMode = Input.MouseModeEnum.Visible;
					Vector2 globalMousePos = GDNode.GetViewport().GetScreenTransform().Origin + _lastMousePosition;
					Input.WarpMouse(globalMousePos);
#if GODOT_WINDOWS
					Input.WarpMouse(globalMousePos); // Workaround for godotengine/godot#119205
#endif

					_currentMovement = Vector3.Zero;
					_currentRotation = Vector2.Zero;
				}
			}
			else if (button.Pressed)
			{
				// Handle scroll zoom/speed
				if (button.ButtonIndex == MouseButton.WheelUp)
				{
					if (_isMouseCaptured)
					{
						_moveSpeed = Mathf.Clamp(MathF.Round(_moveSpeed * 2, 1), 2, 1024);
					}
					else
					{
						SnapForward();
					}
				}
				else if (button.ButtonIndex == MouseButton.WheelDown)
				{
					if (_isMouseCaptured)
					{
						_moveSpeed = Mathf.Clamp(MathF.Round(_moveSpeed / 2), 2, 1024);
					}
					else
					{
						SnapBackward();
					}
				}
			}
		}
		else if (_isMouseCaptured)
		{
			if (@event is InputEventMouseMotion motion)
			{
				_currentRotation = new(-motion.ScreenRelative.X, motion.ScreenRelative.Y);
			}
		}

		if (@event.IsActionPressed("camera_snap_forward"))
		{
			SnapForward();
		}
		else if (@event.IsActionPressed("camera_snap_backward"))
		{
			SnapBackward();
		}

#if CREATOR
		if (@event.IsActionPressed("freelook_goto_target"))
		{
			MoveToSelected();
		}
#endif
	}

	private void SnapForward()
	{
		Position += Forward * _moveSpeed / 10;
	}

	private void SnapBackward()
	{
		Position += Forward * -_moveSpeed / 10;
	}

	public void ReceiveDragTouchInput(InputEventScreenDrag dragEvent)
	{
		_targetRotation += new Vector3(dragEvent.Relative.Y / -5, dragEvent.Relative.X / -5, 0) * Sensitivity * 2;
		LimitRotation();
	}

	private void LimitRotation()
	{
		if (_targetRotation.X > 89)
		{
			_targetRotation.X = 89;
		}

		if (_targetRotation.X < -89)
		{
			_targetRotation.X = -89;
		}
	}

	[ScriptMethod]
	public bool IsPositionInView(Vector3 pos)
	{
		return Camera3D.IsPositionInFrustum(pos.Flip());
	}

	[ScriptMethod]
	public bool IsPositionBehind(Vector3 pos)
	{
		return Camera3D.IsPositionBehind(pos.Flip());
	}

	[ScriptMethod]
	public RayResult? ViewportPointToRay(Vector2 pos, Instance[]? ignoreList = null, float maxDistance = 10000f)
	{
		Viewport viewport = GDNode.GetViewport();
		Vector2 size = viewport.GetVisibleRect().Size;
		Vector2 screenPos = new(pos.X * size.X, pos.Y * size.Y);
		Vector3 rayOrigin = Camera3D.ProjectRayOrigin(screenPos);
		Vector3 rayDir = Camera3D.ProjectRayNormal(screenPos);
		return Root.Environment.Raycast(rayOrigin.Flip(), rayDir.Flip(), maxDistance, ignoreList);
	}

	[ScriptMethod]
	public RayResult? ScreenPointToRay(Vector2 pos, Instance[]? ignoreList = null, float maxDistance = 10000f)
	{
		pos = pos.Flip();
		Vector3 rayOrigin = Camera3D.ProjectRayOrigin(pos);
		Vector3 rayDir = Camera3D.ProjectRayNormal(pos);
		return Root.Environment.Raycast(rayOrigin.Flip(), rayDir.Flip(), maxDistance, ignoreList);
	}

	[ScriptMethod]
	public Vector2 ViewportToScreenPoint(Vector2 pos)
	{
		Viewport viewport = GDNode.GetViewport();
		Vector2 size = viewport.GetVisibleRect().Size;
		return new Vector2(pos.X * size.X, pos.Y * size.Y).Flip();
	}

	[ScriptMethod]
	public Vector3 ViewportToWorldPoint(Vector2 pos)
	{
		Viewport viewport = GDNode.GetViewport();
		Vector2 size = viewport.GetVisibleRect().Size;
		Vector2 screenPos = new(pos.X * size.X, pos.Y * size.Y);
		Vector3 origin = Camera3D.ProjectRayOrigin(screenPos);
		Vector3 direction = Camera3D.ProjectRayNormal(screenPos);
		return (origin + direction * Camera3D.Near).Flip();
	}

	[ScriptMethod]
	public Vector2 WorldToViewportPoint(Vector3 pos)
	{
		pos = pos.Flip();
		Viewport viewport = GDNode.GetViewport();
		Vector2 screenPos = Camera3D.UnprojectPosition(pos);
		Vector2 size = viewport.GetVisibleRect().Size;
		return new Vector2(screenPos.X / size.X, screenPos.Y / size.Y);
	}

	[ScriptMethod]
	public Vector2 WorldToScreenPoint(Vector3 pos)
	{
		pos = pos.Flip();
		Vector2 unprojected = Camera3D.UnprojectPosition(pos);
		return unprojected.Flip();
	}

	[ScriptMethod]
	public Vector2 ScreenToViewportPoint(Vector2 pos)
	{
		pos = pos.Flip();
		Viewport viewport = GDNode.GetViewport();
		if (viewport == null)
			return Vector2.Zero;

		Vector2 size = viewport.GetVisibleRect().Size;
		return new Vector2(pos.X / size.X, pos.Y / size.Y);
	}

	[ScriptMethod]
	public Vector3 ScreenToWorldPoint(Vector2 pos)
	{
		pos = pos.Flip();
		Vector3 rayOrigin = Camera3D.ProjectRayOrigin(new(pos.X, pos.Y));
		Vector3 rayDir = Camera3D.ProjectRayNormal(new(pos.X, pos.Y));
		return (rayOrigin + rayDir * Camera3D.Near).Flip();
	}


#if CREATOR
	public void MoveToSelected()
	{
		Instance[] targets = [.. Root.CreatorContext.Selections.SelectedInstances];

		if (targets.Length == 0)
			return;

		// Calculate bounding box of all selected instances
		Aabb combinedBounds = new();
		bool first = true;

		foreach (Instance t in targets)
		{
			if (t is Dynamic dyn)
			{
				Aabb bounds = dyn.CalculateBounds();
				if (first)
				{
					combinedBounds = bounds;
					first = false;
				}
				else
				{
					combinedBounds = combinedBounds.Merge(bounds);
				}
			}
		}

		if (first) // No valid bounds found
			return;

		Vector3 center = combinedBounds.GetCenter();
		Vector3 size = combinedBounds.Size;

		float radius = size.Length() * 0.5f;

		float fovRadians = Mathf.DegToRad(FOV);
		float distance = radius / Mathf.Tan(fovRadians * 0.5f);

		distance *= 1.2f;

		distance = Mathf.Max(distance, radius + 2.0f);

		Vector3 currentDir;

		Vector3 currentPos = GDNode3D.GlobalPosition;
		Vector3 toCamera = currentPos - center;

		if (toCamera.Length() < 0.1f)
		{
			currentDir = new Vector3(1, 1, 1).Normalized();
		}
		else
		{
			currentDir = toCamera.Normalized();
		}

		Vector3 newPosition = center + currentDir * distance;

		GDNode3D.GlobalPosition = newPosition;
		GDNode3D.LookAt(center, Vector3.Up);

		GDNode3D.RotateObjectLocal(Vector3.Up, Mathf.Pi);
	}

	public Vector3 GetPlacementPosition(Instance[]? ignoreList = null)
	{
		if (World.Current == null) throw new InvalidOperationException("World is null");
		Transform3D globalTransform = GetGlobalTransform();
		Vector3 origin = globalTransform.Origin;
		Vector3 direction = globalTransform.Basis.Z;

		Datamodel.Environment.RayResult? hit = GetPlacementRay(ignoreList);

		if (hit != null)
		{
			return hit.Value.Position;
		}
		else
		{
			return (origin + direction * 10f).Flip();
		}
	}

	public RayResult? GetPlacementRay(Instance[]? ignoreList = null)
	{
		if (World.Current == null) throw new InvalidOperationException("World is null");
		Transform3D globalTransform = GetGlobalTransform();
		Vector3 origin = globalTransform.Origin;
		Vector3 direction = globalTransform.Basis.Z;

		return World.Current.Environment.Raycast(origin.Flip(), direction.Flip(), 20, ignoreList);
	}
#endif
}
