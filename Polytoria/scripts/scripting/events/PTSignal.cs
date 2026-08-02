// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting;


[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
public class PTSignal : IScriptObject
{
	public event Action? Subscribed;
	public event Action? Unsubscribed;

	private readonly List<PTCallback> _ptCallbacks = [];

	private readonly HashSet<PTCallback> _ptSet = [];

	private static readonly Dictionary<Script, List<PTSignal>> _subscribedScripts = [];

	public void Invoke(params object?[]? args)
	{
		InvokeDirect(args ?? []);
	}

	public void InvokeDirect(object?[] args)
	{
		for (int i = _ptCallbacks.Count - 1; i >= 0; i--)
		{
			PTCallback? cb = _ptCallbacks[i];
			if (cb is null || cb.Disposed)
			{
				_ptCallbacks.RemoveAt(i);
				if (cb is not null) _ptSet.Remove(cb);
				continue;
			}

			try { cb.InvokeDirect(args); }
			catch (Exception ex) { GD.PushError($"PTCallback Length: {args.Length} : " + ex.ToString()); }
		}
	}

	private static List<PTSignal> GetSignalListFromScript(Script s)
	{
		if (!_subscribedScripts.TryGetValue(s, out List<PTSignal>? signals))
		{
			signals = [];
			_subscribedScripts[s] = signals;
		}
		return signals;
	}

	private void AddThisSignalToScript(Script s)
	{
		List<PTSignal> signals = GetSignalListFromScript(s);
		if (!signals.Contains(this))
		{
			signals.Add(this);
		}
	}

	private void RemoveThisSignalFromScript(Script s)
	{
		List<PTSignal> signals = GetSignalListFromScript(s);
		signals.Remove(this);
	}

	[ScriptMethod]
	public PTSignalConnection Connect(PTCallback action)
	{
		PTSignalConnection sc = new() { Callback = action, Signal = this };

		if (!_ptSet.Add(action)) return sc;
		_ptCallbacks.Add(action);
		if (action.FromScript != null)
		{
			AddThisSignalToScript(action.FromScript);
		}
		Subscribed?.Invoke();

		return sc;
	}

	public void Connect(Action action)
	{
		PTCallback cb = new(_ => action()) { OriginalDelegate = action };
		Connect(cb);
	}

	public void Connect(Action<object> action)
	{
		PTCallback cb = new(args => action(args?.Length > 0 ? args[0]! : null!)) { OriginalDelegate = action };
		Connect(cb);
	}

	public void Connect(Delegate del)
	{
		if (del is Action a) { Connect(a); return; }
		if (del is Action<object?[]> aArgs) { Connect(aArgs); return; }

		if (_ptCallbacks.Any(c => c.OriginalDelegate == del))
		{
			GD.PushWarning("This delegate already exists");
			return;
		}

		int paramCount = del.Method.GetParameters().Length;
		bool takesArray = paramCount == 1 && del.Method.GetParameters()[0].ParameterType == typeof(object[]);

		PTCallback cb = new(args => del.DynamicInvoke(takesArray ? [args] : args)) { OriginalDelegate = del };
		Connect(cb);
	}

	[ScriptMethod]
	public void Disconnect(PTCallback action)
	{
		if (!_ptSet.Remove(action)) return;
		_ptCallbacks.Remove(action);
		ScriptService.FreePTCallback(action);

		if (action.FromScript != null)
		{
			RemoveThisSignalFromScript(action.FromScript);
		}

		Unsubscribed?.Invoke();
	}

	public void Disconnect(Action action)
	{
		var cb = _ptCallbacks.FirstOrDefault(c => c.OriginalDelegate?.Equals(action) == true);
		if (cb == null) return;
		Disconnect(cb);
	}

	public void Disconnect(Action<object?[]> action)
	{
		var cb = _ptCallbacks.FirstOrDefault(c => c.OriginalDelegate?.Equals(action) == true);
		if (cb == null) return;
		Disconnect(cb);
	}

	public void Disconnect(Delegate del)
	{
		if (del is Action a) { Disconnect(a); return; }
		if (del is Action<object?[]> aArgs) { Disconnect(aArgs); return; }

		var cb = _ptCallbacks.FirstOrDefault(c => c.OriginalDelegate == del);
		if (cb == null) return;
		Disconnect(cb);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(PTSignal? _)
	{
		return "<PTSignal>";
	}

	[ScriptMethod]
	public async Task<object?[]> Wait()
	{
		TaskCompletionSource<object?[]> tcs = new();
		Once(args => tcs.TrySetResult(args ?? []));
		return await tcs.Task;
	}

	[ScriptMethod]
	public void Once(PTCallback action)
	{
		PTCallback? handler = null;
		handler = new(args =>
		{
			if (handler != null)
			{
				Disconnect(handler);
			}
			action.InvokeDirect(args);
			handler = null;
		})
		{ FromScript = action.FromScript };

		Connect(handler);
	}

	public void Once(Action<object> action)
	{
		PTCallback? cb = null;
		cb = new PTCallback(args =>
		{
			Disconnect(cb!);
			action.Invoke(args?.Length > 0 ? args[0]! : null!);
		})
		{ OriginalDelegate = action };
		Connect(cb);
	}

	public void Once(Action<object?[]> action)
	{
		PTCallback? cb = null;
		cb = new PTCallback(args =>
		{
			Disconnect(cb!);
			action.Invoke(args ?? []);
		})
		{ OriginalDelegate = action };
		Connect(cb);
	}

	public void Once(Delegate del)
	{
		if (del is Action<object> a) { Once(a); return; }
		if (del is Action<object?[]> a2) { Once(a2); return; }

		PTCallback? cb = null;
		cb = new PTCallback(args =>
		{
			Disconnect(cb!);
			del.DynamicInvoke(args ?? []);
		})
		{ OriginalDelegate = del };
		Connect(cb);
	}

	public void DisconnectAll()
	{
		List<Script> keys = [.. _subscribedScripts.Keys];
		foreach (var key in keys)
		{
			if (_subscribedScripts.TryGetValue(key, out var list))
			{
				list.Remove(this);
				if (list.Count == 0)
				{
					_subscribedScripts.Remove(key);
				}
			}
		}

		// Free all Lua callbacks
		foreach (var cb in _ptCallbacks)
		{
			if (cb != null && !cb.Disposed)
			{
				ScriptService.FreePTCallback(cb);
			}
		}

		_ptCallbacks.Clear();
		_ptSet.Clear();
	}

	/// <summary>
	/// Disconnect all callbacks related to the target script
	/// </summary>
	/// <param name="s"></param>
	public void DisconnectFromScript(Script s)
	{
		for (int i = _ptCallbacks.Count - 1; i >= 0; i--)
		{
			PTCallback? cb = _ptCallbacks[i];
			if (cb is null || cb.Disposed || cb.FromScript == s)
			{
				_ptCallbacks.RemoveAt(i);
				if (cb is not null) _ptSet.Remove(cb);
				continue;
			}
		}
	}

	/// <summary>
	/// Cleanup all PTSignals from target script
	/// </summary>
	/// <param name="s"></param>
	public static void CleanupScript(Script s)
	{
		if (_subscribedScripts.TryGetValue(s, out List<PTSignal>? signals))
		{
			foreach (PTSignal signal in signals.ToArray())
			{
				signal.DisconnectFromScript(s);
			}
			_subscribedScripts.Remove(s);
		}
	}
}

public struct PTSignalConnection() : IScriptObject
{
	internal PTSignal Signal = null!;
	internal PTCallback Callback = null!;

	[ScriptMethod]
	public readonly void Disconnect()
	{
		Signal.Disconnect(Callback);
	}
}

public class PTSignal<T1> : PTSignal { }
public class PTSignal<T1, T2> : PTSignal { }
public class PTSignal<T1, T2, T3> : PTSignal { }
public class PTSignal<T1, T2, T3, T4> : PTSignal { }
