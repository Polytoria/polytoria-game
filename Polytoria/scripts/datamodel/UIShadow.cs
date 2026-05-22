// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIShadow : Instance
{
	private ShadowLayer[] _layers = [new ShadowLayer()];
	private ShadowPanel[] _panels = [];
	private UIField? _parentView;

	private sealed class ShadowPanel
	{
		public Panel Panel = null!;
		public StyleBoxFlat StyleBox = null!;
	}

	[Editable, ScriptProperty]
	public ShadowLayer[] Layers
	{
		get => _layers;
		set
		{
			_layers = value;
			if (_parentView != null && !IsHidden)
				RebuildPanels();
			OnPropertyChanged();
		}
	}

	public override void EnterTree()
	{
		if (Parent is UIField field)
			Setup(field);
		base.EnterTree();
	}

	public override void ExitTree()
	{
		Teardown();
		base.ExitTree();
	}

	public override void PreDelete()
	{
		Teardown();
		base.PreDelete();
	}

	public override void HiddenChanged(bool to)
	{
		if (to)
		{
			Teardown();
		}
		else if (Parent is UIField field)
		{
			Setup(field);
		}
		base.HiddenChanged(to);
	}

	private void Setup(UIField parent)
	{
		if (_panels.Length > 0 && _parentView == parent)
		{
			ApplyAll();
			return;
		}

		Teardown();

		_parentView = parent;
		_parentView.PropertyChanged.Connect(OnParentPropertyChanged);
		_parentView.TransformChanged.Connect(OnParentTransformChanged);

		RebuildPanels();
	}

	private void Teardown()
	{
		ShadowPanel[] oldPanels = _panels;
		_panels = [];

		if (_parentView != null)
		{
			_parentView.PropertyChanged.Disconnect(OnParentPropertyChanged);
			_parentView.TransformChanged.Disconnect(OnParentTransformChanged);
			_parentView = null;
		}

		foreach (ShadowPanel sp in oldPanels)
			sp.Panel.QueueFree();
	}

	private void RebuildPanels()
	{
		if (_parentView == null) return;

		foreach (ShadowPanel sp in _panels)
			sp.Panel.QueueFree();

		int count = _layers.Length;
		_panels = new ShadowPanel[count];

		for (int i = 0; i < count; i++)
		{
			Panel panel = UIField.CreateOverlayPanel();
			StyleBoxFlat sb = new() { AntiAliasing = true, AntiAliasingSize = 2 };
			panel.AddThemeStyleboxOverride("panel", sb);

			if (_layers[i].BlendMode != ShadowBlendMode.Normal)
			{
				panel.Material = new CanvasItemMaterial
				{
					BlendMode = _layers[i].BlendMode switch
					{
						ShadowBlendMode.Add => CanvasItemMaterial.BlendModeEnum.Add,
						ShadowBlendMode.Subtract => CanvasItemMaterial.BlendModeEnum.Sub,
						_ => CanvasItemMaterial.BlendModeEnum.Mix,
					}
				};
			}

			_parentView.NodeControl.AddChild(panel);
			_parentView.NodeControl.MoveChild(panel, 0);

			_panels[i] = new ShadowPanel { Panel = panel, StyleBox = sb };
		}

		ApplyAll();
	}

	private void ApplyAll()
	{
		if (_parentView == null || IsHidden || _panels.Length == 0)
			return;

		var corners = _parentView.InternalGetCorners();
		int cTL = Mathf.RoundToInt(corners.TopLeft);
		int cTR = Mathf.RoundToInt(corners.TopRight);
		int cBL = Mathf.RoundToInt(corners.BottomLeft);
		int cBR = Mathf.RoundToInt(corners.BottomRight);

		for (int i = 0; i < _layers.Length && i < _panels.Length; i++)
		{
			ApplyLayerStyle(i, _layers[i], cTL, cTR, cBL, cBR);
			_panels[i].Panel.ZIndex = -(i + 1);
		}
	}

	private void ApplyLayerStyle(int index, ShadowLayer layer, int cTL, int cTR, int cBL, int cBR)
	{
		StyleBoxFlat sb = _panels[index].StyleBox;

		float a = layer.Color.A;
		sb.BgColor = new Color(0, 0, 0, 0);
		sb.ShadowSize = Mathf.RoundToInt(layer.Radius);
		sb.ShadowColor = new Color(layer.Color.R, layer.Color.G, layer.Color.B, a);
		sb.ShadowOffset = layer.Offset;

		int spread = Mathf.RoundToInt(layer.Spread);
		sb.ExpandMarginLeft = spread;
		sb.ExpandMarginRight = spread;
		sb.ExpandMarginTop = spread;
		sb.ExpandMarginBottom = spread;

		sb.CornerRadiusTopLeft = cTL;
		sb.CornerRadiusTopRight = cTR;
		sb.CornerRadiusBottomLeft = cBL;
		sb.CornerRadiusBottomRight = cBR;
	}

	private void OnParentPropertyChanged(string propertyName)
	{
		if (propertyName is "CornerRadius")
			ApplyAll();
	}

	private void OnParentTransformChanged()
	{
		ApplyAll();
	}
}
