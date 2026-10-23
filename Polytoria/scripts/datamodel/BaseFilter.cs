// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Datamodel.Services;
using System;

namespace Polytoria.Datamodel;

public partial class BaseFilter : Instance
{
	private bool _isEnabled;
	private bool _refreshing = false;
	private Shader _filterShader = null!;
	internal PTCompositorLayer CompositorLayer { get; set; } = null!;

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

	public override Node CreateGDNode()
	{
		_filterShader = LoadFilter();
		CompositorLayer = new(_filterShader);
		return CompositorLayer;
	}

	public override void EnterTree()
	{
		UpdateVisibility();
		base.EnterTree();
	}

	public override void ExitTree()
	{
		DetachFilter();
		base.ExitTree();
	}

	public override void PostIndexMove()
	{
		UpdateVisibility();
		base.PostIndexMove();
	}

	public override void PreDelete()
	{
		DetachFilter();
		_filterShader.Dispose();
		base.PreDelete();
	}

	// <summary>
	// Called to create a new compositor layer with the desired effect.
	// This method should be overridden.
	// </summary>
	protected virtual Shader LoadFilter() => new();

	// <summary>
	// Used to update shader uniforms. Should be called at the end of every setter.
	// </summary>
	public void SetFilterUniform(string uniformName, Variant uniformValue) => CompositorLayer.SetUniform(uniformName, uniformValue);

	private static bool HasCompositor(Instance inst)
	{
		return inst is UIViewport || inst is World || inst.IsHidden;
	}

	private PTCompositor? FindCompositor()
	{
		Instance? parent = Parent;
		while (parent != null)
		{
			switch (parent)
			{
				case World world: return world.Compositor;
				case UIViewport view: return view.Compositor;
				default:
					parent = parent.Parent;
					continue;
			}
		}

		return null;
	}

	private void GetNearestFilter(out BaseFilter? nearestFilter, out bool isAbove)
	{
		Instance? highInstance = this;
		Instance? lowInstance = this;

		static Instance? StepUp(Instance at)
		{
			if (at.Parent == null) return null;
			if (at.Index > 0)
			{
				int idx = at.Index - 1;
				GD.Print(at.Index);
				Instance? sibling = at.Parent.Children?[idx];

				while (sibling != null)
				{
					while (HasCompositor(sibling))
					{
						if (idx == 0) return sibling.Parent;
						sibling = sibling.Parent.Children[--idx];
					}

					if (sibling.Children.Count > 0)
					{
						idx = sibling.Children.Count - 1;
						sibling = sibling.Children[idx];
					}
					else break;
				}

				return sibling;
			}

			return HasCompositor(at.Parent) ? null : at.Parent;
		}

		static Instance? StepDown(Instance at)
		{
			if (at.Children.Count > 0) return at.Children[0];
			else if (at.Parent != null)
			{
				int idx = at.Index + 1;
				Instance? parent = at.Parent;

				while (idx >= parent.Children.Count)
				{
					if (HasCompositor(parent)) return null;
					idx = parent.Index + 1;
					parent = parent.Parent;
					if (parent == null) return null;
				}

				return parent.Children[idx];
			}
			return null;
		}

		GD.Print("Searching for nearest filter...");
		do
		{
			if (highInstance != null)
			{
				highInstance = StepUp(highInstance);
				if (highInstance is BaseFilter filter)
				{
					nearestFilter = filter;
					isAbove = true;
					GD.Print("Found filter above");
					return;
				}
			}

			if (lowInstance != null)
			{
				lowInstance = StepDown(lowInstance);
				if (lowInstance is BaseFilter filter)
				{
					nearestFilter = filter;
					isAbove = false;
					GD.Print("Found filter below");
					return;
				}
			}
		} while (highInstance != null && lowInstance != null);
		GD.Print("Failed to find filter");

		nearestFilter = null;
		isAbove = false;
		return;
	}

	private void AttachFilter()
	{
		if (CompositorLayer.Compositor != null) DetachFilter();

		PTCompositor? compositor = FindCompositor();
		if (compositor != null)
		{
			if (compositor.RootLayer == null)
			{
				CompositorLayer.AttachAsRoot(compositor);
				GD.Print("Attached to root layer");
			}
			else
			{
				GetNearestFilter(out BaseFilter? nearestFilter, out bool isAbove);
				if (nearestFilter != null)
				{
					if (isAbove) CompositorLayer.AttachAfter(nearestFilter.CompositorLayer);
					else CompositorLayer.AttachBefore(nearestFilter.CompositorLayer);

					GD.Print(isAbove ? "Attached below" : "Attached above");
				}
				else
				{
					CompositorLayer.AttachAsRoot(compositor);
					GD.Print("Attached as root");
				}
			}
		}
		else CompositorLayer.Detach();
	}

	private void DetachFilter() => CompositorLayer.Detach();

	// TODO: Disable the filter within PTCompositorLayer to prevent scanning again
	private void UpdateVisibility()
	{
		if (!_refreshing)
		{
			_refreshing = true;
			PT.CallDeferred(() =>
			{
				if (!IsHidden && _isEnabled) AttachFilter();
				else DetachFilter();
				_refreshing = false;
			});
		}
	}
}
