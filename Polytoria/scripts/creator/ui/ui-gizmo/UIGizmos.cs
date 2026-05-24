// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.UI.Gizmos;

public partial class UIGizmos : CanvasLayer
{
	private const string GizmoBoxPath = "res://scenes/client/ui/ui-gizmo/gizmo_box.tscn";
	private readonly Dictionary<UIField, UIGizmoBox> _fieldToBox = [];

	private GuidelineDrawer? _guidelineDrawer;

	private void EnsureGuidelineDrawer()
	{
		if (_guidelineDrawer != null) return;
		_guidelineDrawer = new GuidelineDrawer();
		AddChild(_guidelineDrawer);
		_guidelineDrawer.SyncSize();
	}

	public UIGizmoBox? AddBox(UIField ui)
	{
		if (_fieldToBox.TryGetValue(ui, out UIGizmoBox? existing)) return existing;

		UIGizmoBox? box;
		try
		{
			box = Globals.CreateInstanceFromScene<UIGizmoBox>(GizmoBoxPath);
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Failed to crearte UIGizmoBox for '{ui.Name}': ", ex);
			return null;
		}

		_fieldToBox[ui] = box;
		box.Target = ui;
		box.Gizmos = this;
		AddChild(box);
		return box;
	}

	public void RemoveBox(UIField ui)
	{
		if (_fieldToBox.TryGetValue(ui, out UIGizmoBox? existing))
		{
			existing.QueueFree();
			_fieldToBox.Remove(ui);
		}
	}

	internal void ShowGuidelines(IReadOnlyList<SnapGuide> guides)
	{
		EnsureGuidelineDrawer();
		_guidelineDrawer.SetGuides(guides);
	}

	internal void HideGuidelines()
	{
		_guidelineDrawer?.ClearGuides();
	}

	internal void ShowMeasurements(IReadOnlyList<MeasureGuide> measures)
	{
		EnsureGuidelineDrawer();
		_guidelineDrawer.SetMeasures(measures);
	}

	internal void HideMeasurements()
	{
		_guidelineDrawer?.ClearMeasures();
	}
}

internal readonly struct SnapGuide
{
	public readonly Vector2 From;
	public readonly Vector2 To;
	public readonly Color Color;

	public SnapGuide(Vector2 from, Vector2 to, Color color)
	{
		From = from;
		To = to;
		Color = color;
	}
}

internal readonly struct MeasureGuide
{
	public readonly Vector2 From;
	public readonly Vector2 To;
	public readonly Color Color;
	public readonly string Text;
	public readonly Vector2 TextPos;

	public MeasureGuide(Vector2 from, Vector2 to, Color color, string text, Vector2 textPos)
	{
		From = from;
		To = to;
		Color = color;
		Text = text;
		TextPos = textPos;
	}
}

internal partial class GuidelineDrawer : Control
{
	private IReadOnlyList<SnapGuide> _guides = Array.Empty<SnapGuide>();
	private IReadOnlyList<MeasureGuide> _measures = Array.Empty<MeasureGuide>();
	private Font? _font;
	private readonly StyleBoxFlat _labelBg = new()
	{
		BgColor = new Color(0.95f, 0.25f, 0.25f, 0.9f),
		CornerRadiusTopLeft = 4,
		CornerRadiusTopRight = 4,
		CornerRadiusBottomLeft = 4,
		CornerRadiusBottomRight = 4,
		ContentMarginLeft = 4,
		ContentMarginRight = 4,
		ContentMarginTop = 2,
		ContentMarginBottom = 2,
	};

	public GuidelineDrawer()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = false;
	}

	public override void _Ready()
	{
		SyncSize();
		if (GetViewport() is Viewport vp)
			vp.SizeChanged += SyncSize;
	}

	public override void _ExitTree()
	{
		if (GetViewport() is Viewport vp)
			vp.SizeChanged -= SyncSize;
	}

	public void SyncSize()
	{
		if (GetViewport() is Viewport vp)
			Size = vp.GetVisibleRect().Size;
	}

	public void SetGuides(IReadOnlyList<SnapGuide> guides)
	{
		_guides = guides;
		QueueRedraw();
	}

	public void ClearGuides()
	{
		if (_guides.Count == 0) return;
		_guides = Array.Empty<SnapGuide>();
		QueueRedraw();
	}

	public void SetMeasures(IReadOnlyList<MeasureGuide> measures)
	{
		_measures = measures;
		QueueRedraw();
	}

	public void ClearMeasures()
	{
		if (_measures.Count == 0) return;
		_measures = Array.Empty<MeasureGuide>();
		QueueRedraw();
	}

	public override void _Draw()
	{
		foreach (SnapGuide guide in _guides)
			DrawLine(guide.From, guide.To, guide.Color, 2);

		if (_measures.Count == 0) return;

		_font ??= GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
		if (_font == null) return;

		int fontSize = 12;
		float fontAscent = _font.GetAscent(fontSize);
		const float padX = 5;
		const float padY = 3;

		foreach (MeasureGuide m in _measures)
		{
			DrawDashedLine(m.From, m.To, m.Color, 1, 4);

			Vector2 textSize = _font.GetStringSize(m.Text, HorizontalAlignment.Left, -1, fontSize);
			Vector2 pillSize = textSize + new Vector2(padX * 2, padY * 2);
			Vector2 pillPos = m.TextPos - pillSize * 0.5f;

			DrawStyleBox(_labelBg, new Rect2(pillPos, pillSize));
			DrawString(_font, new Vector2(pillPos.X + padX, pillPos.Y + padY + fontAscent), m.Text,
				HorizontalAlignment.Left, -1, fontSize, Colors.White);
		}
	}
}
