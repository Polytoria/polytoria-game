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
	
	private Polytoria.Datamodel.Camera _worldCamera = null;
	private SubViewportContainer _rect = null!;
	private Camera3D _axisCamera = null!;
	private RayCast3D _raycast = null!;
	private Area3D _cube = null!;
	
	private Label3D _highlighted = null;
	
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
		_worldCamera = Overlay.World.CreatorContext.Freelook;
		_pivot.GlobalRotation = _worldCamera.Camera3D.GlobalRotation;
	}

	public void HandleInput(InputEvent @event)
	{
		ProjectMouse();
		if (!_raycast.IsColliding()) 
		{
			Unhighlight();
			return;
		}
		
		Vector3 normal = _raycast.GetCollisionNormal();
		if (@event is InputEventMouseMotion _) HighlightLabel(normal);
		else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } _)
		{
			if (_worldCamera != null)
			{
				Vector3 up = Mathf.Abs(normal.Y) > 0.9f ? Vector3.Back : Vector3.Up;
				Basis targetBasis = Basis.LookingAt(-normal, up);
				_worldCamera.Rotation = targetBasis.GetEuler() * (180f / Mathf.Pi);
			}
		}
	}
	
	private void ProjectMouse()
	{
		var mousePos = _rect.GetLocalMousePosition();
		var rayOrigin = _axisCamera.ProjectRayOrigin(mousePos);
		var rayNormal = _axisCamera.ProjectRayNormal(mousePos);
		_raycast.Position = _axisCamera.ToLocal(rayOrigin);
		_raycast.TargetPosition = _axisCamera.ToLocal(rayOrigin + rayNormal * 10f);
		_raycast.ForceRaycastUpdate();
	}
	
	private readonly Dictionary<Vector3I, string> labelSuffixes = new Dictionary<Vector3I, string>
	{
		{ Vector3I.Left, "Right" },
		{ Vector3I.Right, "Left" },
		{ Vector3I.Up, "Top" },
		{ Vector3I.Down, "Bottom" },
		{ Vector3I.Forward, "Front" },
		{ Vector3I.Back, "Back" }
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

		Unhighlight();
		_highlighted = toHighlight;
		_highlighted.Modulate = new Color(0x2196f3ff);
	}
}
