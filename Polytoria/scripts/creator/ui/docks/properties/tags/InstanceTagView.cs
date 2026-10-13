// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Creator.UI;

public partial class InstanceTagView : Control
{
	private const string TagLabelPath = "res://scenes/creator/tags/tag_label.tscn";
	public List<Instance> Targets = [];
	[Export] private Control _container = null!;
	[Export] private LineEdit _newTagEdit = null!;
	[Export] private Button _plusButton = null!;
	[Export] private Control _tagLayout = null!;
	[Export] private Control _blankLayout = null!;

	private string _searchFilter = "";

	public override void _Ready()
	{
		base._Ready();

		_newTagEdit.TextSubmitted += OnTextSubmitted;
		_newTagEdit.TextChanged += OnSearch;
		_plusButton.Pressed += AddNewTag;
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		Clear();
	}

	private void OnTextSubmitted(string text)
	{
		AddNewTag();
	}

	private void OnSearch(string newText)
	{
		_searchFilter = newText;
		RefreshTagDisplay();
	}

	public void AddNewTag()
	{
		if (Targets.Count == 0) return;
		if (string.IsNullOrEmpty(_newTagEdit.Text)) return;

		foreach (Instance instance in Targets)
		{
			instance.AddTag(_newTagEdit.Text);
		}

		_newTagEdit.Text = "";
		Show(Targets);
	}

	public void Clear()
	{
		_blankLayout.Visible = true;
		_tagLayout.Visible = false;

		foreach (Node item in _container.GetChildren())
		{
			item.QueueFree();
		}
	}

	private void RefreshTagDisplay()
	{
		foreach (Node child in _container.GetChildren())
		{
			if (child is not TagLabel label) continue;
			bool matchesSearch = string.IsNullOrEmpty(_searchFilter) ||
								 label.Text.Contains(_searchFilter, StringComparison.CurrentCultureIgnoreCase);
			label.Visible = matchesSearch;
		}
	}

	public void Show(List<Instance> instances)
	{
		Clear();
		Targets = instances;

		if (Targets.Count == 0) return;

		_blankLayout.Visible = false;
		_tagLayout.Visible = true;

		HashSet<string> allTags = [];
		foreach (var tag in Targets.SelectMany(instance => instance.Tags))
		{
			allTags.Add(tag);
		}

		foreach (string tag in allTags)
		{
			TagLabel label = Globals.CreateInstanceFromScene<TagLabel>(TagLabelPath);
			label.Text = tag;

			// Check if all targets have this tag
			bool allHaveTag = Targets.All(inst => inst.Tags.Contains(tag));

			// Dim or mark tags that aren't on all instances
			if (!allHaveTag)
			{
				label.Modulate = new Color(1, 1, 1, 0.5f);
			}

			label.DeleteRequested += () =>
			{
				// Remove tag from all instances that have it
				foreach (Instance instance in Targets)
				{
					instance.RemoveTag(tag);
				}
				Show(Targets);
			};

			_container.AddChild(label);
		}
	}
}
