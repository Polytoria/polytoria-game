// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIScrollView : UIContainer
{
	private TouchScrollContainer _scrollContainer = null!;
	private ScrollModeEnum _horizontalScrollMode = ScrollModeEnum.Auto;
	private ScrollModeEnum _verticalScrollMode = ScrollModeEnum.Auto;
	private float _scrollBarThickness;
	private Color _scrollBarColor;

	private readonly StyleBoxFlat _grabberStyle = new() { AntiAliasing = true };
	private readonly StyleBoxEmpty _trackStyle = new();
	private static readonly string[] GrabberStyleNames = ["grabber", "grabber_highlight", "grabber_pressed"];

	[Editable, ScriptProperty, DefaultValue(ScrollModeEnum.Auto)]
	public ScrollModeEnum HorizontalScrollMode
	{
		get => _horizontalScrollMode;
		set
		{
			_horizontalScrollMode = value;

			_scrollContainer.HorizontalScrollMode = value switch
			{
				ScrollModeEnum.Disabled => ScrollContainer.ScrollMode.Disabled,
				ScrollModeEnum.Auto => ScrollContainer.ScrollMode.Auto,
				ScrollModeEnum.AlwaysShow => ScrollContainer.ScrollMode.ShowAlways,
				ScrollModeEnum.NeverShow => ScrollContainer.ScrollMode.ShowNever,
				_ => ScrollContainer.ScrollMode.Auto
			};

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(ScrollModeEnum.Auto)]
	public ScrollModeEnum VerticalScrollMode
	{
		get => _verticalScrollMode;
		set
		{
			_verticalScrollMode = value;

			_scrollContainer.VerticalScrollMode = value switch
			{
				ScrollModeEnum.Disabled => ScrollContainer.ScrollMode.Disabled,
				ScrollModeEnum.Auto => ScrollContainer.ScrollMode.Auto,
				ScrollModeEnum.AlwaysShow => ScrollContainer.ScrollMode.ShowAlways,
				ScrollModeEnum.NeverShow => ScrollContainer.ScrollMode.ShowNever,
				_ => ScrollContainer.ScrollMode.Auto
			};

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float ScrollBarThickness
	{
		get => _scrollBarThickness;
		set
		{
			_scrollBarThickness = Mathf.Max(0, value);
			ApplyScrollBarStyle();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color ScrollBarColor
	{
		get => _scrollBarColor;
		set
		{
			_scrollBarColor = value;
			ApplyScrollBarStyle();
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return new TouchScrollContainer();
	}

	public override void InitGDNode()
	{
		_scrollContainer = (TouchScrollContainer)GDNode;
		base.InitGDNode();
	}

	public override void Init()
	{
		base.Init();

		ClipDescendants = true;
		ScrollBarThickness = 6;
		ScrollBarColor = new(0, 0, 0, 0.5f);

		// Scroll to start at the last frame
		Callable.From(() =>
		{
			Callable.From(() =>
			{
				_scrollContainer.ScrollHorizontal = 0;
				_scrollContainer.ScrollVertical = 0;
			}).CallDeferred();
		}).CallDeferred();
	}

	private void ApplyScrollBarStyle()
	{
		_grabberStyle.BgColor = _scrollBarColor;
		_grabberStyle.SetCornerRadiusAll(Mathf.CeilToInt(_scrollBarThickness / 2));

		StyleScrollBar(_scrollContainer.GetVScrollBar(), new(_scrollBarThickness, 0));
		StyleScrollBar(_scrollContainer.GetHScrollBar(), new(0, _scrollBarThickness));
	}

	private void StyleScrollBar(ScrollBar bar, Vector2 minSize)
	{
		bar.CustomMinimumSize = minSize;
		bar.AddThemeStyleboxOverride("scroll", _trackStyle);
		bar.AddThemeStyleboxOverride("scroll_focus", _trackStyle);
		foreach (string styleName in GrabberStyleNames)
		{
			bar.AddThemeStyleboxOverride(styleName, _grabberStyle);
		}
	}

	[ScriptEnum("UIScrollMode")]
	public enum ScrollModeEnum
	{
		Disabled,
		Auto,
		AlwaysShow,
		NeverShow
	}
}
