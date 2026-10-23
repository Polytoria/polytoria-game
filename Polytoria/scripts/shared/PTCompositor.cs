// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Shared;

public partial class PTCompositor : Control
{
	private Texture2D? _renderResult = null;
	private PTCompositorLayer? _rootLayer = null;
	private PTCompositorLayer? _lastLayer = null;
	private List<Material> _materials = new();

	public SubViewport RootViewport { get; private set; } = null!;
	public PTCompositorLayer? RootLayer
	{
		get => _rootLayer;
		internal set
		{
			if (_rootLayer == value) return;
			_rootLayer = value;
			if (_rootLayer != null) _OnResize();
		}
	}
	public PTCompositorLayer? LastLayer
	{
		get => _lastLayer;
		internal set
		{
			if (_lastLayer == value) return;
			_lastLayer = value;
			if (_lastLayer != null)
			{
				_OnResize();
				_renderResult = _lastLayer.GetTexture();
			}
			else _renderResult = null;
		}
	}

	public PTCompositor()
	{
		RootViewport = new();
		_OnResize();
	}
	public PTCompositor(SubViewport customRoot)
	{
		RootViewport = customRoot;
		_OnResize();
	}

	public override void _EnterTree() => Resized += _OnResize;
	public override void _ExitTree() => Resized -= _OnResize;
	public override void _Ready() => _OnResize();
	public override void _Process(double _)
	{
		_OnResize();
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_renderResult != null)
		{
			DrawTextureRect(
				_renderResult,
				new(Position.X, Position.Y, Position.X + Size.X, Position.Y + Size.Y),
				false
			);
		}
	}

	private void _OnResize()
	{
		PTCompositorLayer? layer = _rootLayer;
		Vector2I compositorSize = new((int)Size.X, (int)Size.Y);
		RootViewport.Size = compositorSize;
		while (layer != null)
		{
			layer.Size = compositorSize;
			layer = layer.Next;
		}
		_renderResult = _lastLayer?.GetTexture();
	}
}

public partial class PTCompositorLayer : SubViewport
{
	private TextureRect _compositorRect = null!;
	private ShaderMaterial _effectMaterial = new();

	internal Texture2D? RenderTexture { get => _compositorRect.Texture; set => _compositorRect.Texture = value; }

	public PTCompositorLayer? Next { get; internal set; } = null;
	public PTCompositorLayer? Last { get; internal set; } = null;
	public PTCompositor? Compositor { get; internal set; } = null;
	public Shader? Effect { get => _effectMaterial.Shader; set => _effectMaterial.Shader = value; }

	public PTCompositorLayer()
	{
		TransparentBg = true;
		Msaa2D = Viewport.Msaa.Disabled;
		RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		CanvasLayer displayLayer = new();
		AddChild(displayLayer);

		_compositorRect = new();
		_compositorRect.Material = _effectMaterial;
		_compositorRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		displayLayer.AddChild(_compositorRect);
	}

	public PTCompositorLayer(Shader effect)
		: this()
	{ Effect = effect; }

	public override void _ExitTree() => _effectMaterial.Dispose();
	public void SetUniform(string uniformName, Variant uniformValue) => _effectMaterial.SetShaderParameter(uniformName, uniformValue);

	public void AttachBefore(PTCompositorLayer layer)
	{
		Detach();
		Compositor = layer.Compositor;
		if (Compositor?.RootLayer == layer) AttachAsRoot(Compositor);
		else
		{
			Next = layer;
			Last = layer.Last;
			layer.Last?.Next = this;
			layer.Last = this;
			if (Last != null) RenderTexture = Last.GetTexture();
		}
	}

	public void AttachAfter(PTCompositorLayer layer)
	{
		Detach();
		Compositor = layer.Compositor;
		Last = layer;
		Next = layer.Next;
		layer.Next?.Last = this;
		layer.Next = this;
		RenderTexture = layer.GetTexture();
		if (Compositor.LastLayer == layer) Compositor.LastLayer = this;
	}

	public void AttachAsRoot(PTCompositor newCompositor)
	{
		Detach();
		Compositor = newCompositor;
		RenderTexture = Compositor.RootViewport.GetTexture();
		if (Compositor.RootLayer != null)
		{
			Next = Compositor.RootLayer;
			Next.RenderTexture = GetTexture();
			Next.Last = this;
			Compositor.RootLayer = this;
		}
		else Compositor.LastLayer = Compositor.RootLayer = this;
	}

	public void Detach()
	{
		RenderTexture = null!;
		Last?.Next = Next;
		Next?.Last = Last;

		if (Compositor != null)
		{
			if (Compositor.RootLayer == this)
			{
				Compositor.RootLayer = Next;
				Next?.RenderTexture = Compositor.RootViewport.GetTexture();
			}
			if (Compositor.LastLayer == this) Compositor.LastLayer = Last;
			Compositor = null;
		}
		Last = null;
		Next = null;
	}
}
