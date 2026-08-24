// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel.Services;

// NOTE: Godot doesn't pass deltatime to the FramePreDraw or FramePostDraw
// signals, so we have to grab it manually using Node.GetProcessDeltaTime()
[Static("Hooks"), ExplorerExclude, SaveIgnore]
public sealed partial class HookService : Instance
{
	[ScriptProperty]
	public PTSignal<double> Updated { get; private set; } = new();
	[ScriptProperty]
	public PTSignal<double> PreRendered { get; private set; } = new();
	[ScriptProperty]
	public PTSignal<double> PostRendered { get; private set; } = new();
	[ScriptProperty]
	public PTSignal<double> PhysicsUpdated { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		SetProcess(true);
		SetPhysicsProcess(true);
	}

	public override void Ready()
	{
		RenderingServer.FramePreDraw += OnFramePreDraw;
		RenderingServer.FramePostDraw += OnFramePostDraw;
		base.Ready();
	}

	public override void PreDelete()
	{
		RenderingServer.FramePreDraw -= OnFramePreDraw;
		RenderingServer.FramePostDraw -= OnFramePostDraw;
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		Updated.Invoke(delta);
		base.Process(delta);
	}

	public override void PhysicsProcess(double delta)
	{
		PhysicsUpdated.Invoke(delta);
		base.PhysicsProcess(delta);
	}

	private void OnFramePreDraw()
	{
		if (!GodotObject.IsInstanceValid(GDNode)) return;
		PreRendered.Invoke(GDNode.GetProcessDeltaTime());
	}

	private void OnFramePostDraw()
	{
		if (!GodotObject.IsInstanceValid(GDNode)) return;
		PostRendered.Invoke(GDNode.GetProcessDeltaTime());
	}
}
