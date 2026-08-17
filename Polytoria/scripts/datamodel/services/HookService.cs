// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using Polytoria.Attributes;
using Polytoria.Scripting;
using Polytoria.Scripting.Luau;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Services;

[Static("Hooks"), ExplorerExclude, SaveIgnore]
public sealed partial class HookService : Instance
{
	private readonly List<QueuedResume> _spawnQueue = [];
	private readonly List<TimedEntry> _timedQueue = [];
	private readonly Dictionary<PTCallback, List<object?[]>> _deferredCallbacks = [];

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
		DrainSpawnQueue();
		DrainTimedQueue();
		DrainDeferredCallbacks();
		base.Process(delta);
	}

	public override void PhysicsProcess(double delta)
	{
		PhysicsUpdated.Invoke(delta);
		base.PhysicsProcess(delta);
	}

	private void OnFramePreDraw()
	{
		PreRendered.Invoke(GDNode.GetProcessDeltaTime());
	}

	private void OnFramePostDraw()
	{
		PostRendered.Invoke(GDNode.GetProcessDeltaTime());
	}

	/// <summary>
	/// Queues a thread's first resumption for the next drain. Used by 'spawn'
	/// so a burst of spawns in one script doesn't cascade into the caller's
	/// own call stack.
	/// </summary>
	internal void EnqueueSpawn(LuaState thread, int threadRef, int numArgs)
	{
		_spawnQueue.Add(new(thread, threadRef, numArgs));
	}

	/// <summary>
	/// Queues resolve to run once Root.UpTime reaches wakeTime.
	/// </summary>
	internal void EnqueueTimed(decimal wakeTime, Action resolve)
	{
		_timedQueue.Add(new(wakeTime, resolve));
	}

	/// <summary>
	/// Queues a PTCallback call for the next drain.
	/// </summary>
	internal void EnqueueCallback(PTCallback callback, object?[] args)
	{
		if (_deferredCallbacks.TryGetValue(callback, out List<object?[]>? calls))
		{
			calls.Add(args);
		}
		else
		{
			_deferredCallbacks[callback] = [args];
		}
	}

	/// <summary>
	/// Dequeues all of a PTCallback's calls from the next drain.
	/// </summary>
	internal void DequeueCallback(PTCallback callback)
	{
		_deferredCallbacks.Remove(callback);
	}

	private void DrainSpawnQueue()
	{
		if (_spawnQueue.Count == 0) return;

		// Snapshot so anything spawned during this drain runs next tick
		QueuedResume[] batch = [.. _spawnQueue];
		_spawnQueue.Clear();

		foreach (QueuedResume entry in batch)
		{
			async void run()
			{
				try
				{
					await LuauProvider.ResumeThread(entry.Thread, null, entry.NumArgs);
				}
				finally
				{
					entry.Thread.Unref(entry.ThreadRef);
				}
			}
			run();
		}
	}

	private void DrainTimedQueue()
	{
		if (_timedQueue.Count == 0) return;

		decimal now = Root.UpTime;
		for (int i = _timedQueue.Count - 1; i >= 0; i--)
		{
			if (_timedQueue[i].WakeTime <= now)
			{
				Action resolve = _timedQueue[i].Resolve;
				_timedQueue.RemoveAt(i);
				resolve();
			}
		}
	}

	private void DrainDeferredCallbacks()
	{
		if (_deferredCallbacks.Count == 0) return;

		// Snapshot so anything spawned during this drain runs next tick
		Dictionary<PTCallback, List<object?[]>> batch = new(_deferredCallbacks);
		_deferredCallbacks.Clear();

		foreach ((PTCallback callback, List<object?[]> calls) in batch)
		{
			if (callback.Disposed) continue;
			foreach (object?[] args in calls)
			{
				try
				{
					callback.InvokeDirect(args);
				}
				catch (Exception ex)
				{
					GD.PushError($"Deferred PTCallback Length: {args.Length} : " + ex.ToString());
				}
			}
		}
	}

	internal readonly struct QueuedResume(LuaState thread, int threadRef, int numArgs)
	{
		public readonly LuaState Thread = thread;
		public readonly int ThreadRef = threadRef;
		public readonly int NumArgs = numArgs;
	}

	internal readonly struct TimedEntry(decimal wakeTime, Action resolve)
	{
		public readonly decimal WakeTime = wakeTime;
		public readonly Action Resolve = resolve;
	}
}
