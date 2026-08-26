// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class DatamodelTest
{
	public World World = null!;

	public DatamodelTest()
	{
		Globals.UseNodes = false;
		World = new();
		World.InitEntry();
		World.Setup();
	}

	[Fact]
	public void SetStaticName_ShouldFail()
	{
		Assert.Throws<InvalidOperationException>(() => World.Name = "Should not be possible");
		Assert.Throws<InvalidOperationException>(() => World.Environment.Name = "Should not");
	}

	[Fact]
	public async Task Test_InstanceFunctions()
	{
		var part = World.New<Part>(World.Environment);
		part.Name = "Part1";

		// Test creation
		Assert.Equal("Part1", part.Name);
		Assert.Equal(World.Environment, part.Parent);

		// Test tagging
		part.AddTag("TagTest");
		Assert.Contains("TagTest", part.Tags);
		part.RemoveTag("TagTest");
		Assert.DoesNotContain("TagTest", part.Tags);

		// Part1
		// * Part2
		// * Part3
		var part2 = World.New<Part>();
		part2.Name = "Part2";
		part2.Parent = part;
		part2.AddTag("Tag2");

		var part3 = World.New<Part>();
		part3.Name = "Part3";
		part3.Parent = part;
		part3.AddTag("Tag3");

		Assert.Equal("Part2", part2.Name);
		Assert.Equal(part, part2.Parent);

		// Find child of part2
		Assert.Equal(part2, part.FindChild("Part2"));
		Assert.Equal(part2, await part.WaitChild("Part2"));
		Assert.Equal(part2, part.FindChildByClass("Part"));
		Assert.Equal(part2, part.FindChildWithTag("Tag2"));

		// Check get children with tag2
		Assert.True(part.GetChildrenWithTag("Tag2").SequenceEqual([part2]));

		Assert.Equal(part, part2.FindAncestorByClass("Part"));

		// Check null
		Assert.Null(part.FindChild("DoesntExist"));
		Assert.Null(part.FindChildByClass("Mesh"));
		Assert.Null(part.FindChildWithTag("Tag"));

		part.Delete();
		Assert.True(part.IsDeleted);
		Assert.True(part2.IsDeleted);
		Assert.True(part3.IsDeleted);
	}

	[Fact]
	public void Test_NetworkPathCacheInvalidation()
	{
		var a = World.New<Part>(World.Environment);
		a.Name = "PathA";
		var b = World.New<Part>();
		b.Name = "PathB";
		b.Parent = a;

		string pathBefore = b.NetworkPath;
		Assert.EndsWith("PathA.PathB", pathBefore);
		Assert.Equal(pathBefore, b.NetworkPath);

		a.Name = "PathRenamed";
		Assert.EndsWith("PathRenamed.PathB", b.NetworkPath);

		var c = World.New<Part>(World.Environment);
		c.Name = "PathC";
		b.Parent = c;
		Assert.EndsWith("PathC.PathB", b.NetworkPath);

		b.Name = "PathB2";
		Assert.EndsWith("PathC.PathB2", b.NetworkPath);

		a.Delete();
		c.Delete();
	}

	[Fact]
	public void Test_NameEnforcement()
	{
		// Environment
		// * Part
		// * Part2
		var part = World.New<Part>();
		part.Name = "Part";
		part.Parent = World.Environment;

		var part2 = World.New<Part>();
		part2.Name = "Part";
		part2.Parent = World.Environment;

		// Check name reenforcement
		Assert.Equal("Part", part.Name);
		Assert.Equal("Part2", part2.Name);

		part.Delete();
		part2.Delete();
	}
}
