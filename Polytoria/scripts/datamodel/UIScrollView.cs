// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIScrollView : UIContainer
{
	private ScrollContainer _scrollContainer = null!;
	private ScrollModeEnum _horizontalScrollMode = ScrollModeEnum.Auto;
	private ScrollModeEnum _verticalScrollMode = ScrollModeEnum.Auto;

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

	public override Node CreateGDNode()
	{
		return new ScrollContainer();
	}

	public override void InitGDNode()
	{
		_scrollContainer = (ScrollContainer)GDNode;
		base.InitGDNode();
	}

	public override void Init()
	{
		base.Init();

		ClipDescendants = true;

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

	[ScriptEnum("UIScrollMode")]
	public enum ScrollModeEnum
	{
		Disabled,
		Auto,
		AlwaysShow,
		NeverShow
	}
}
