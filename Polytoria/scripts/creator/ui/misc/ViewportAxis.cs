// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace Polytoria.Creator.UI;

public partial class ViewportAxis : Node
{
	[Export] public WorldContainerOverlay Overlay = null!;
	[Export] private Node3D _pivot = null!;
	[Export] private Node _container = null!;

	private Polytoria.Datamodel.Camera? _worldCamera = null;
	private SubViewportContainer _rect = null!;
	private Camera3D _axisCamera = null!;
	private RayCast3D _raycast = null!;
	private Area3D _cube = null!;
	private Label3D? _highlighted = null;

	private Vector3 _tweenStart, _tweenTarget;
	private float _tweenProgress = 1f;
	private const float _tweenDuration = .2f;

	private readonly Dictionary<Key, (Vector3 noMod, Vector3 withMod)> KeyToRotation = new()
	{
		{ Key.Kp1, (new Vector3(0, 0, 0),  new Vector3(0, 180, 0)) },
		{ Key.Kp3, (new Vector3(0, 90, 0),  new Vector3(0, -90, 0)) },
		{ Key.Kp7, (new Vector3(-90, 180, 0),  new Vector3(90, 0, 0)) }
	};

	public override void _Ready()
	{
		_rect = GetNode<SubViewportContainer>("TextureRect");
		_axisCamera = _pivot.GetNode<Camera3D>("Camera3D");
		_raycast = _axisCamera.GetNode<RayCast3D>("RayCast3D");
		_cube = _container.GetNode<Area3D>("Cube");

		_raycast.Enabled = true;
	}

	public override void _Process(double delta)
	{
		if (_tweenProgress < 1f)
		{
			_tweenProgress = Mathf.Min(_tweenProgress + (float)delta / _tweenDuration, 1f);
			float t = Mathf.SmoothStep(0, 1, _tweenProgress);
			_worldCamera?.Rotation = _tweenStart.Lerp(_tweenTarget, t);
		}

		_worldCamera = Overlay.World.CreatorContext.Freelook;
		_pivot.GlobalRotation = _worldCamera.Camera3D.GlobalRotation;
	}

	public bool HandleInput(InputEvent @event)
	{
		if (@event is InputEventMouse && (!ProjectMouse() || !_raycast.IsColliding()))
		{
			Unhighlight();
			return false;
		}

		Vector3 normal = _raycast.GetCollisionNormal();
		switch (@event)
		{
			case InputEventMouseMotion:
				HighlightLabel(normal);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
				if (_worldCamera != null)
				{
					Vector3 up = Mathf.Abs(normal.Y) > 0.9f ? Vector3.Back : Vector3.Up;
					Basis targetBasis = Basis.LookingAt(-normal, up);
					RotateWorldCamera(targetBasis.GetEuler() * (180f / Mathf.Pi));
				}
				return true;
			case InputEventKey eventKey:
				if (eventKey.Echo || !eventKey.Pressed) break;
				if (!KeyToRotation.TryGetValue(eventKey.Keycode, out var rotations)) break;
				RotateWorldCamera(eventKey.CtrlPressed ? rotations.withMod : rotations.noMod);
				return true;
		}
		return false;
	}

	private bool ProjectMouse()
	{
		var mousePos = _rect.GetLocalMousePosition();
		if (mousePos.X < 0 && mousePos.Y < 0) return false;

		var rayOrigin = _axisCamera.ProjectRayOrigin(mousePos);
		var rayNormal = _axisCamera.ProjectRayNormal(mousePos);
		_raycast.Position = _axisCamera.ToLocal(rayOrigin);
		_raycast.TargetPosition = _axisCamera.ToLocal(rayOrigin + rayNormal * 10f);
		_raycast.ForceRaycastUpdate();
		return true;
	}

	private readonly Dictionary<Vector3I, string> labelSuffixes = new()
	{
		{ Vector3I.Left, "Left" },
		{ Vector3I.Right, "Right" },
		{ Vector3I.Up, "Top" },
		{ Vector3I.Down, "Bottom" },
		{ Vector3I.Forward, "Back" },
		{ Vector3I.Back, "Front" }
	};

	private void Unhighlight()
	{
		if (_highlighted == null) return;
		_highlighted.Modulate = Colors.Black;
		_highlighted = null;
	}

	private void HighlightLabel(Vector3 normal)
	{
		var normalI = new Vector3I((int)normal.X, (int)normal.Y, (int)normal.Z);
		var labelPath = "MeshInstance3D/Label3D" + labelSuffixes[normalI];
		var toHighlight = _cube.GetNode<Label3D>(labelPath);
		if (_highlighted == toHighlight) return;

		Color color = normalI switch
		{
			{ X: not 0 } => new Color(0xd60000ff),
			{ Y: not 0 } => new Color(0x26d165ff),
			_ => new Color(0x0048ffff)
		};

		Unhighlight();
		_highlighted = toHighlight;
		_highlighted.Modulate = color;
	}

	private static float Shorten(float from, float to)
	{
		float diff = (to - from + 540f) % 360f - 180f;
		return from + diff;
	}

	private void RotateWorldCamera(Vector3 rotation)
	{
		_tweenStart = _worldCamera!.Rotation;
		_tweenTarget = new Vector3(
			Shorten(_tweenStart.X, rotation.X),
			Shorten(_tweenStart.Y, rotation.Y),
			Shorten(_tweenStart.Z, rotation.Z)
		);
		if (_tweenStart == _tweenTarget) return;
		_tweenProgress = 0f;
	}
}
