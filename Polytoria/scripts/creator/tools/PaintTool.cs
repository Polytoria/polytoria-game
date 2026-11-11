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
public partial class PaintTool : CreatorTool, IImmediateTool
{
	public static PaintTool Singleton = null!;

	public PaintTool()
	{
		Singleton = this;
	}

	public SelectionBox HoverBox = null!;

	public Color TargetColor => CreatorService.Interface.TargetPartColor;

	public override void ProcessInput(InputEvent e, Dynamic? hoveringOn)
	{
		if (hoveringOn != null && hoveringOn is Entity && !hoveringOn.Locked)
		{
			HoverBox.SelectionColor = TargetColor;
			HoverBox.Target = hoveringOn;
		}
		else
		{
			HoverBox.Target = null;
		}
	}

	public void Apply(World Root, Instance instance)
	{
		Apply(Root, [instance]);
	}

	public void Apply(World Root, IEnumerable<Instance> instances)
	{
		List<Entity> entities = [];
		CreatorHistory history = Root.CreatorContext.History;
		foreach (Instance i in instances)
		{
			if (i is Entity e) entities.Add(e);
		}
		if (entities.Count == 0) return;

		Color newC = TargetColor;
		Dictionary<Entity, Color> oldColors = [];
		foreach (Entity e in entities) oldColors[e] = e.Color;

		history.NewAction("Paint Part");
		history.AddDoCallback(new((_) =>
		{
			foreach (Entity e in entities) e.Color = newC;
		}));
		history.AddUndoCallback(new((_) =>
		{
			foreach (Entity e in entities) e.Color = oldColors[e];
		}));
		history.CommitAction();
	}
}
