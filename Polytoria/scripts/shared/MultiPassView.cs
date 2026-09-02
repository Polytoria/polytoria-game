// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Shared;

public partial class MultiPassView : Control
{
	private List<Material> _materials = new();
	private List<SubViewport> _renderViews = new();
	private ViewportTexture? _renderedProduct = null;

	public SubViewport? InitialView
	{
		get => _renderViews.Count != 0 ? _renderViews[0] : null;
		set
		{
			if (value != null)
			{
				if (_renderViews.Count == 0)
					_renderViews.Add(value);
				else
					_renderViews[0] = value;
			}
			else
			{
				if (_renderViews.Count != 0)
					_renderViews[0] = null!;
			}
		}
	}

	public SubViewport? ProcessedView
	{ get => _renderViews.Count != 0 ? _renderViews[_renderViews.Count - 1] : null; }

	public MultiPassView()
	{ }
	public MultiPassView(SubViewport initialView)
	{
		InitialView = initialView;
	}

	public override void _EnterTree()
	{
		RecomputeViews();
	}

	public override void _Process(double delta)
	{
		if (ProcessedView != null)
		{
			Vector2I viewportSize = new((int)Size.X, (int)Size.Y);
			foreach (SubViewport view in _renderViews)
			{
				view.Size = viewportSize;
			}

			_renderedProduct = ProcessedView.GetTexture();
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (_renderedProduct != null)
		{
			DrawTextureRect(
				_renderedProduct,
				new(Position.X, Position.Y, Position.X + Size.X, Position.Y + Size.Y),
				false
			);
		}
	}

	private SubViewport CreateRenderPass(Material mat, Viewport previousView)
	{
		SubViewport view = new()
		{
			Size = InitialView != null ? InitialView.Size : new(800, 800),
			TransparentBg = true,
			Msaa2D = Viewport.Msaa.Disabled
		};

		CanvasLayer displayLayer = new();
		view.AddChild(displayLayer);

		TextureRect rect = new() { Material = mat };
		rect.Texture = previousView.GetTexture();
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		displayLayer.AddChild(rect);
		return view;
	}

	// TODO: This is naive. Make a better method of doing this.
	// Ideally, something that doesn't clear all viewports.
	private void RecomputeViews()
	{
		for (int i = _renderViews.Count - 1; i > 0; i--)
		{
			_renderViews[i].QueueFree();
			_renderViews.RemoveAt(i);
		}

		if (ProcessedView != null)
		{
			foreach (Material mat in _materials)
			{
				SubViewport view = CreateRenderPass(mat, ProcessedView);
				AddChild(view, true, Node.InternalMode.Back);
				view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
				_renderViews.Add(view);
			}

			_renderedProduct = ProcessedView.GetTexture();
		}
	}

	public void AddRenderPass(Material mat, int idx = 0)
	{
		if (idx <= 0)
		{
			_materials.Add(mat);
			RecomputeViews();
		}
		else
		{
			_materials.Insert(idx, mat);
			RecomputeViews();
		}
	}

	public void SetRenderPasses(Material[] mats)
	{
		ClearRenderPasses();
		_materials = new(mats);
		RecomputeViews();
	}

	public void RemoveRenderPass(Material mat)
	{
		_materials.Remove(mat);
		RecomputeViews();
	}

	public void ClearRenderPasses()
	{
		_materials = new();
		RecomputeViews();
	}
}
