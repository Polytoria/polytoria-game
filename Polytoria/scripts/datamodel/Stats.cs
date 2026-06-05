// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Static("Stats")]
public sealed partial class Stats : Instance
{
	public PTSignal<Stat> StatAdded { get; private set; } = new();
	public PTSignal<Stat> StatRemoved { get; private set; } = new();
	public PTSignal<Stat> StatPropertyChanged { get; private set; } = new();

	public override void Init()
	{
		ChildAdded.Connect(OnChildAdded);
		ChildRemoved.Connect(OnChildRemoved);
		base.Init();
	}

	[ScriptMethod]
	public Stat[] GetStats()
	{
		List<Stat> stats = [];

		foreach (Instance item in GetChildren())
		{
			if (item is Stat s)
			{
				stats.Add(s);
			}
		}

		return [.. stats];
	}

	private void OnChildAdded(Instance instance)
	{
		if (instance is Stat stat)
		{
			stat.PropertyChanged.Connect(() =>
			{
				StatPropertyChanged.Invoke(stat);
			});
			StatAdded.Invoke(stat);
		}
	}

	private void OnChildRemoved(Instance instance)
	{
		if (instance is Stat stat)
		{
			StatRemoved.Invoke(stat);
		}
	}
}
