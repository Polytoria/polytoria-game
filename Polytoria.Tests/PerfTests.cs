// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;

namespace Polytoria.Tests;

public class PerfTests
{
	public World World;

	public PerfTests()
	{
		Globals.UseNodes = false;
		World = new();
		World.InitEntry();
		World.Setup();
	}

	private static long MeasureAllocations(int iterations, Action action)
	{
		action();
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < iterations; i++)
		{
			action();
		}
		return GC.GetAllocatedBytesForCurrentThread() - before;
	}

	[Fact]
	public void SignalInvoke_NoListeners_AllocationBudget()
	{
		PTSignal<double> signal = new();
		long allocated = MeasureAllocations(10_000, () => signal.Invoke(1.0));
		Assert.True(allocated < 1_000, $"Allocated {allocated} bytes");
	}

	[Fact]
	public void SignalInvoke_OneListener_AllocationBudget()
	{
		PTSignal<double> signal = new();
		double sum = 0;
		signal.Connect(() => sum += 1);
		long allocated = MeasureAllocations(10_000, () => signal.Invoke(1.0));
		Assert.True(allocated < 2_000_000, $"Allocated {allocated} bytes");
		Assert.True(sum > 0);
	}

	[Fact]
	public void FindChild_Miss_AllocationBudget()
	{
		Part parent = World.New<Part>(World.Environment);
		parent.Name = "FindChildPerfRoot";
		for (int i = 0; i < 200; i++)
		{
			Part child = World.New<Part>();
			child.Name = $"Child{i}";
			child.Parent = parent;
		}
		long allocated = MeasureAllocations(10_000, () => parent.FindChild("DoesNotExist"));
		Assert.True(allocated < 1_000, $"Allocated {allocated} bytes");
		parent.Delete();
	}

	[Fact]
	public void TypeLookup_Hit_AllocationBudget()
	{
		long allocated = MeasureAllocations(10_000, () => Globals.GetTypeByName("Part"));
		Assert.True(allocated < 1_000, $"Allocated {allocated} bytes");
	}

	[Fact]
	public void TypeLookup_Miss_AllocationBudget()
	{
		long allocated = MeasureAllocations(10_000, () => Globals.GetTypeByName("NoSuchClassEver"));
		Assert.True(allocated < 1_000, $"Allocated {allocated} bytes");
	}

	[Fact]
	public void NetworkPath_DeepChain_AllocationBudget()
	{
		Part current = World.New<Part>(World.Environment);
		current.Name = "Depth0";
		Part leaf = current;
		for (int i = 1; i < 10; i++)
		{
			Part next = World.New<Part>();
			next.Name = $"Depth{i}";
			next.Parent = leaf;
			leaf = next;
		}
		string expected = leaf.NetworkPath;
		long allocated = MeasureAllocations(10_000, () => _ = leaf.NetworkPath);
		Assert.Equal(expected, leaf.NetworkPath);
		Assert.True(allocated < 1_000, $"Allocated {allocated} bytes");
		current.Delete();
	}

	[Fact]
	public void GetDescendants_LargeTree_AllocationBudget()
	{
		Part root = World.New<Part>(World.Environment);
		root.Name = "DescendantsPerfRoot";
		for (int i = 0; i < 30; i++)
		{
			Part branch = World.New<Part>();
			branch.Name = $"Branch{i}";
			branch.Parent = root;
			for (int j = 0; j < 30; j++)
			{
				Part twig = World.New<Part>();
				twig.Name = $"Twig{j}";
				twig.Parent = branch;
			}
		}
		Assert.Equal(930, root.GetDescendants().Length);
		long allocated = MeasureAllocations(100, () => root.GetDescendants());
		Assert.True(allocated < 16_000_000, $"Allocated {allocated} bytes");
		root.Delete();
	}
}
