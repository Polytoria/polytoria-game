// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings.Appliers;
using Polytoria.Creator.Settings;
using Polytoria.Creator.UI.Gizmos;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;

namespace Polytoria.Creator.UI;

public sealed partial class WorldContainer : SubViewportContainer
{
	private const string ContainerOverlayPath = "res://scenes/creator/misc/game_container_overlay.tscn";
	private readonly SubViewport _subViewport = null!;
	private string? _draggingFile;
	private bool _dragFileShown = false;
	private Instance? _draggingModel;
	public World World = null!;
	public WorldContainerOverlay Overlay = null!;
	public UIGizmos UIGizmos = null!;
	internal event Action<InputEvent>? GodotInputEvent;

	public WorldContainer(World game)
	{
		World = game;
		World3D world3D = new();
		AddChild(_subViewport = new() { OwnWorld3D = false, HandleInputLocally = true, World3D = world3D, Msaa3D = Viewport.Msaa.Msaa4X }, true);
		game.Container = this;
		game.World3D = world3D;
		_subViewport.AddChild(game.GDNode, true);

		_subViewport.AddChild(UIGizmos = new() { Layer = 20 });

		Stretch = true;
		FocusMode = FocusModeEnum.All;
		MouseFilter = MouseFilterEnum.Stop;
		MouseTarget = true;

		MouseExited += OnMouseExited;

		Overlay = Globals.CreateInstanceFromScene<WorldContainerOverlay>(ContainerOverlayPath);
		Overlay.World = game;
		Overlay.Container = this;
		_subViewport.AddChild(Overlay);
	}

	public override void _Ready()
	{
		if (CreatorSettingsService.Instance != null)
		{
			var applier = CreatorSettingsService.Instance.GetNodeOrNull<GraphicsSettingsApplier>(GraphicsSettingsApplier.NodeName);
			if (applier != null)
			{
				applier.RenderViewport = _subViewport;
				applier.ApplyViewportSettings();
			}
		}

		// Call on next frame to actually grab focus
		Callable.From(GrabFocus).CallDeferred();
		base._Ready();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn && btn.Pressed)
		{
			GrabFocus();
		}
		_subViewport.PushInput(@event);
		GodotInputEvent?.Invoke(@event);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		_subViewport.PushInput(@event);
		GodotInputEvent?.Invoke(@event);
		base._UnhandledKeyInput(@event);
	}

	public override bool _PropagateInputEvent(InputEvent @event)
	{
		return false;
	}

	private void OnMouseExited()
	{
		if (_draggingFile != null)
		{
			_draggingFile = null;
			_draggingModel?.Delete();
			_draggingModel = null;
			_dragFileShown = false;
		}
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (World.SessionType != World.SessionTypeEnum.Creator) return false;
		IDragDataUnion? dragData = DragData.Deserialize(data);

		if (dragData is FileDragData fd)
		{
			string targetFile = fd.Files[0];
			string fileExt = targetFile.GetExtension();

			if (fileExt == Globals.ModelFileExtension)
			{
				if (_draggingFile == targetFile && _draggingModel != null && _draggingModel is Dynamic dyn)
				{
					Datamodel.Environment.RayResult? ray = World.CreatorContext.Freelook.ScreenPointToRay(World.Input.MousePosition, [dyn]);
					if (ray.HasValue)
					{
						dyn.Position = ray.Value.Position;
					}
					else
					{
						dyn.Position = World.CreatorContext.Freelook.GetPlacementPosition([dyn]);
					}
				}
				else if (!_dragFileShown)
				{
					_draggingFile = targetFile;
					ShowInsertModel();
				}
				return true;
			}
		}

		return false;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		_draggingFile = null;
		_draggingModel = null;
		_dragFileShown = false;
		GrabFocus();
		base._DropData(atPosition, data);
	}

	private async void ShowInsertModel()
	{
		if (_draggingFile == null) return;
		_dragFileShown = true;
		_draggingModel = await World.LinkedSession.InsertModel(_draggingFile, World.Environment);
	}
}
