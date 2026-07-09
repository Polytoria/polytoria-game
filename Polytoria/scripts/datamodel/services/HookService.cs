// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel.Services;

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
		base.Ready();
		// NOTE: Godot doesn't pass deltatime to the frame_pre_draw or
		// frame_post_draw signals, so we have to grab it manually using
		// Node.GetProcessDeltaTime()
		RenderingServer.Singleton.Connect(
			RenderingServer.SignalName.FramePreDraw,
			Callable.From(OnFramePreDraw)
		);
		RenderingServer.Singleton.Connect(
			RenderingServer.SignalName.FramePostDraw,
			Callable.From(OnFramePostDraw)
		);
	}

	public override void Process(double delta)
	{
		Updated.Invoke(delta);
		base.Process(delta);
	}

	public override void PhysicsProcess(double delta)
	{
		PhysicsUpdated.Invoke(delta);
		base.Process(delta);
	}

	private void OnFramePreDraw()
	{
		PreRendered.Invoke(GDNode.GetProcessDeltaTime());
	}

	private void OnFramePostDraw()
	{
		PostRendered.Invoke(GDNode.GetProcessDeltaTime());
	}
}
