// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Datamodel.Services;

namespace Polytoria.Datamodel;

public partial class BaseFilter : Instance
{
	internal ShaderMaterial _shaderMaterial = new();
	internal virtual Shader _filterShader
	{ get => null!; }

	private bool _isEnabled;
	private MultiPassView? _modifiedView = null;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool IsEnabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			UpdateVisibility();
		}
	}

	public override void Init()
	{
		_shaderMaterial.Shader = _filterShader;
		UpdateVisibility();
		UpdateFilter();
		SetProcess(true);
		base.Init();
	}

	public override void PostReparent()
	{
		UpdateVisibility();
		base.PostReparent();
	}

	public override void PreDelete()
	{
		_shaderMaterial.Dispose();
		base.PreDelete();
	}

	private MultiPassView? GetView()
	{
		Instance? parent = Parent;
		while (parent != null)
		{
			if (parent is World)
				return ((World)parent).RootView;
			else if (parent is UIViewport)
				return ((UIViewport)parent).SubView;
			parent = parent.Parent;
		}

		return null;
	}

	private int GetDescendingFilters(Instance inst)
	{
		// Like GetDescendants(), yet doesn't traverse viewports.
		int totalFilters = 0;

		foreach (Instance child in inst.GetChildren())
		{
			if (child is BaseFilter)
				totalFilters++;
			if (!(child is UIViewport))
				totalFilters += GetDescendingFilters(child);
		}

		return totalFilters;
	}

	private int GetIndex(Instance inst)
	{
		int totalFilters = 0;
		Instance? parent = inst.Parent;

		while (parent != null)
		{
			for (int i = inst.Index - 1; i >= 0; i--)
			{
				Instance other = parent.Children[i];
				if (other is BaseFilter)
					totalFilters++;
				if (!(other is UIViewport))
					totalFilters += GetDescendingFilters(other);
			}

			inst = parent;
			parent = parent.Parent;
		}

		GD.Print(totalFilters);
		return totalFilters;
	}

	private void ApplyFilter()
	{
		MultiPassView? currentView = GetView();
		if (currentView != _modifiedView)
		{
			RemoveFilter();

			_modifiedView = currentView;
			if (_modifiedView != null)
			{
				_modifiedView.AddRenderPass(_shaderMaterial, GetIndex(this));
			}
		}
	}

	private void RemoveFilter()
	{
		if (_modifiedView != null)
		{
			_modifiedView.RemoveRenderPass(_shaderMaterial);
			_modifiedView = null;
		}
	}

	private void UpdateVisibility()
	{
		if (!IsHidden && _isEnabled)
			ApplyFilter();
		else
			RemoveFilter();
	}

	protected virtual void UpdateFilter() { }
}
