// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator;
using Polytoria.Creator.Spatial;
using Polytoria.Creator.Tools;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using System.Collections.Generic;

namespace Polytoria.Creator.Tools;

[Abstract]
public partial class BrushTool : CreatorTool, IImmediateTool
{
	public static BrushTool Singleton = null!;

	public BrushTool()
	{
		Singleton = this;
	}

	public Part.PartMaterialEnum TargetMaterial => CreatorService.Interface.TargetPartMaterial;

	public void Apply(World Root, Instance instance)
	{
		Apply(Root, [instance]);
	}

	public void Apply(World Root, IEnumerable<Instance> instances)
	{
		List<Part> parts = [];
		CreatorHistory history = Root.CreatorContext.History;
		foreach (Instance i in instances)
		{
			if (i is Part p) parts.Add(p);
		}
		if (parts.Count == 0) return;

		Part.PartMaterialEnum newM = TargetMaterial;
		Dictionary<Part, Part.PartMaterialEnum> oldMaterials = [];
		foreach (Part p in parts) oldMaterials[p] = p.Material;

		history.NewAction("Brush Part");
		history.AddDoCallback(new((_) =>
		{
			foreach (Part p in parts) p.Material = newM;
		}));
		history.AddUndoCallback(new((_) =>
		{
			foreach (Part p in parts) p.Material = oldMaterials[p];
		}));
		history.CommitAction();
	}
}
