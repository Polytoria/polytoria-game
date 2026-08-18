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
	private readonly List<TimedEntry> _timedQueue = [];
	private readonly Dictionary<PTCallback, DeferredCallbackEntry> _deferredCallbacks = [];
	private readonly List<Action> _nextTickQueue = [];

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
		DrainTimedQueue();
		DrainDeferredCallbacks();
		DrainNextTickQueue();
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
		EnqueueNextTick(() =>
		{
			async void run()
			{
				try { await LuauProvider.ResumeThread(thread, null, numArgs); }
				finally { thread.Unref(threadRef); }
			}
			run();
		});
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
		if (_deferredCallbacks.TryGetValue(callback, out DeferredCallbackEntry? entry))
		{
			entry.Calls.Add(args);
		}
		else
		{
			_deferredCallbacks[callback] = new([args]);
		}
	}

	/// <summary>
	/// Frees a callback's resources after any pending drain invocations have fired.
	/// Invocations queued before disconnect are allowed to fire, then the refs are released.
	/// </summary>
	internal void RequestFreeCallback(PTCallback callback)
	{
		if (_deferredCallbacks.TryGetValue(callback, out DeferredCallbackEntry? entry))
		{
			entry.PendingFree = true;
		}
		else
		{
			ScriptService.FreePTCallbackDirect(callback);
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

		Dictionary<PTCallback, DeferredCallbackEntry> batch = new(_deferredCallbacks);
		_deferredCallbacks.Clear();

		foreach ((PTCallback callback, DeferredCallbackEntry entry) in batch)
		{
			if (!callback.Disposed)
			{
				foreach (object?[] args in entry.Calls)
				{
					try { callback.InvokeDirect(args); }
					catch (Exception ex) { GD.PushError($"Deferred PTCallback Length: {args.Length} : " + ex.ToString()); }
				}
			}

			// Release only after invocations have fired, and only if not already freed mid-invocation
			if (entry.PendingFree && !callback.Disposed)
			{
				ScriptService.FreePTCallbackDirect(callback);
			}
		}
	}

	/// <summary>
	/// Queues an action to run on the next Process() tick, after this tick's deferred
	/// signal callbacks have drained (used by wait/delay's default path)
	/// </summary>
	internal void EnqueueNextTick(Action resolve)
	{
		_nextTickQueue.Add(resolve);
	}

	private void DrainNextTickQueue()
	{
		if (_nextTickQueue.Count == 0) return;

		Action[] batch = [.. _nextTickQueue];
		_nextTickQueue.Clear();

		foreach (Action resolve in batch)
		{
			try { resolve(); }
			catch (Exception ex) { GD.PushError("Deferred next-tick action: " + ex); }
		}
	}

	internal readonly struct TimedEntry(decimal wakeTime, Action resolve)
	{
		public readonly decimal WakeTime = wakeTime;
		public readonly Action Resolve = resolve;
	}

	private sealed class DeferredCallbackEntry(List<object?[]> calls)
	{
		public readonly List<object?[]> Calls = calls;
		public bool PendingFree;
	}
}
