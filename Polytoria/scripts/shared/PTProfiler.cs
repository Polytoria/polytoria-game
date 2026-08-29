// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Polytoria.Shared;

public static class PTProfiler
{
	public static bool Enabled;

	private static readonly Dictionary<string, ScopeStat> _stats = [];
	private static readonly object _lock = new();

	private class ScopeStat
	{
		public long Ticks;
		public long Count;
	}

	public readonly ref struct ProfileScope
	{
		private readonly string? _name;
		private readonly long _start;

		internal ProfileScope(string name)
		{
			_name = name;
			_start = Stopwatch.GetTimestamp();
		}

		public void Dispose()
		{
			if (_name == null) return;
			long elapsed = Stopwatch.GetTimestamp() - _start;
			lock (_lock)
			{
				if (!_stats.TryGetValue(_name, out ScopeStat? stat))
				{
					stat = new ScopeStat();
					_stats[_name] = stat;
				}
				stat.Ticks += elapsed;
				stat.Count++;
			}
		}
	}

	public static ProfileScope Scope(string name)
	{
		return Enabled ? new ProfileScope(name) : default;
	}

	public static string Dump()
	{
		StringBuilder sb = new();
		sb.AppendLine("[PROFILE] scope                          total ms      calls    us/call");
		lock (_lock)
		{
			List<KeyValuePair<string, ScopeStat>> rows = [.. _stats];
			rows.Sort((a, b) => b.Value.Ticks.CompareTo(a.Value.Ticks));
			foreach ((string name, ScopeStat stat) in rows)
			{
				double ms = stat.Ticks * 1000.0 / Stopwatch.Frequency;
				double usPerCall = stat.Count > 0 ? ms * 1000.0 / stat.Count : 0;
				sb.AppendLine($"[PROFILE] {name,-30} {ms,10:F1} {stat.Count,10} {usPerCall,10:F1}");
			}
		}
		return sb.ToString();
	}

	public static void Reset()
	{
		lock (_lock)
		{
			_stats.Clear();
		}
	}
}
