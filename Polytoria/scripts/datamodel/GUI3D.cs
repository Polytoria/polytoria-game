// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class GUI3D : Dynamic
{
	private readonly StandardMaterial3D _material = new();
	private MeshInstance3D _mesh = null!;

	private bool _shaded = true;
	private bool _faceCamera = false;
	private bool _transparent = false;

	private bool _mouseInArea = false;
	private Vector2? _lastPos;

	private SubViewport _subViewport = null!;
	private PlaneMesh _plane = null!;
	private Area3D _area = null!;

	[Editable, ScriptProperty]
	public bool Shaded
	{
		get => _shaded;
		set
		{
			_shaded = value;
			_material.ShadingMode = value ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded;
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty]
	public bool FaceCamera
	{
		get => _faceCamera;
		set
		{
			_faceCamera = value;
			_material.BillboardMode = value ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled;
			UpdateSize();
			SetProcess(value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Transparent
	{
		get => _transparent;
		set
		{
			_transparent = value;
			_material.Transparency = value ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
			_subViewport.TransparentBg = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public Vector2 AbsoluteSize => _subViewport.Size;

	public override Node CreateGDNode()
	{
		Node gui3D = Globals.LoadNetworkedObjectScene("GUI3D")!;
		_subViewport = new() { HandleInputLocally = false };
		_area = gui3D.GetNode<Area3D>("Area3D");
		gui3D.AddChild(_subViewport);
		return gui3D;
	}

	public override void InitGDNode()
	{
		SlotNode = _subViewport;
		base.InitGDNode();
	}

	public override void Init()
	{
		_material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_mesh = GDNode.GetNode<MeshInstance3D>("Mesh");
		_plane = (PlaneMesh)_mesh.Mesh;
		_mesh.MaterialOverride = _material;
		_material.AlbedoTexture = _subViewport.GetTexture();

		Shaded = true;
		FaceCamera = false;
		Transparent = false;

		TransformChanged += UpdateCanvasSize;

		_area.MouseEntered += OnAreaMouseEnter;

		base.Init();
	}

	public override void Ready()
	{
		UpdateCanvasSize();
		base.Ready();
	}

	private void UpdateCanvasSize()
	{
		_subViewport.Size = new((int)(Size.X * 512), (int)(Size.Y * 512));
		RecomputeChildTransforms();
	}

	public override void PreDelete()
	{
		_area.MouseEntered -= OnAreaMouseEnter;
		TransformChanged -= UpdateCanvasSize;
		base.PreDelete();
	}

	public override void EnterTree()
	{
		Root.Input.GodotInputEvent += OnInput;
		base.EnterTree();
	}

	public override void ExitTree()
	{
		Root.Input.GodotInputEvent -= OnInput;
		base.ExitTree();
	}

	public void OnInput(InputEvent @event)
	{
		if (_mouseInArea)
		{
			GDNode.GetViewport().SetInputAsHandled();
			if (@event is InputEventMouse m)
			{
				HandleMouse(m);
			}
			else
			{
				_subViewport.PushInput(@event);
			}
		}
	}

	private void HandleMouse(InputEventMouse @event)
	{
		Vector3? pre = FindMouse(@event.GlobalPosition);
		if (pre == null) { _mouseInArea = false; return; }

		Vector3 mousePos3D = pre.Value;
		mousePos3D = _area.GlobalTransform.AffineInverse() * mousePos3D;

		Vector2 mousePos2D = new(mousePos3D.X, mousePos3D.Y);
		Vector2 viewportPos = new(Mathf.Remap(mousePos2D.X, 0.5f, -0.5f, 0, AbsoluteSize.X), Mathf.Remap(mousePos2D.Y, 0.5f, -0.5f, 0, AbsoluteSize.Y));

		@event.Position = viewportPos;
		@event.GlobalPosition = viewportPos;

		if (_lastPos == null)
		{
			_lastPos = viewportPos;
		}

		if (@event is InputEventMouseMotion em)
		{
			em.Relative = viewportPos - _lastPos.Value;
		}

		_lastPos = viewportPos;
		_subViewport.PushInput(@event);
	}

	private void OnAreaMouseEnter()
	{
		_mouseInArea = true;
	}

	protected void RecomputeChildTransforms()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is UIField uifield)
			{
				uifield.RecomputeTransform();
			}
		}
	}

	// https://github.com/godotengine/godot-demo-projects/blob/3.5-9e68af3/viewport/gui_in_3d/gui_3d.gd#L61
	private Vector3? FindMouse(Vector2 globalPosition)
	{
		Camera3D camera = Globals.Singleton.GetViewport().GetCamera3D();
		Vector3 from = camera.ProjectRayOrigin(globalPosition);
		float dist = FindFurtherDistanceTo(camera.Transform.Origin);
		Vector3 to = from + camera.ProjectRayNormal(globalPosition) * dist;

		PhysicsDirectSpaceState3D spaceState = Root.World3D.DirectSpaceState;
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollisionMask = _area.CollisionLayer;
		query.CollideWithAreas = true;
		query.CollideWithBodies = false;

		Godot.Collections.Dictionary result = spaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			return (Vector3)result["position"];
		}
		else
		{
			return null;
		}
	}

	private float FindFurtherDistanceTo(Vector3 origin)
	{
		List<Vector3> edges =
		[
			_area.ToGlobal(new Vector3(Size.X / 2, Size.Y / 2, 0)),
			_area.ToGlobal(new Vector3(Size.X / 2, -Size.Y / 2, 0)),
			_area.ToGlobal(new Vector3(-Size.X / 2, Size.Y / 2, 0)),
			_area.ToGlobal(new Vector3(-Size.X / 2, -Size.Y / 2, 0))
		];

		// Get the furthest distance between the camera and collision to avoid raycasting too far or too short
		float farDist = 0;
		float tempDist;

		foreach (Vector3 edge in edges)
		{
			tempDist = origin.DistanceTo(edge);
			if (tempDist > farDist)
			{
				farDist = tempDist;
			}
		}

		return farDist;
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_mesh.Scale = newSize;
		_area.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	private void UpdateSize()
	{
		if (FaceCamera)
		{
			_plane.Size = new(Size.X, Size.Y);
		}
		else
		{
			_plane.Size = Vector2.One;
		}
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (FaceCamera)
		{
			Camera3D cam = Globals.Singleton.GetViewport().GetCamera3D();
			Vector3 look = cam.ToGlobal(new(0, 0, -100)) - cam.GlobalTransform.Origin;

			_area.LookAt(look);
			_area.RotateObjectLocal(Vector3.Back, cam.Rotation.Z);
		}
		else
		{
			_area.Rotation = Vector3.Zero;
		}
	}
}
