// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Mobile.UI;

public partial class WorldsGrid : GridContainer
{
	private const int SkeletonCount = 9;
	private const float MinCardWidth = 104f;
	private const float Spacing = 12f;
	private const int MinColumns = 2;

	private const float LoadMoreDistance = 600f;
	private const ulong RetryDelayMsec = 3000;

	[Export] private ScrollContainer _scroll = null!;

	private float _cardWidth = MinCardWidth;

	private readonly HashSet<int> _shownIDs = [];
	private readonly List<Control> _skeletons = [];
	private int _page;
	private bool _hasMorePages = true;
	private bool _loading;
	private ulong _retryAt;

	public override void _Ready()
	{
		AddThemeConstantOverride("h_separation", (int)Spacing);
		Resized += UpdateColumns;

		VScrollBar bar = _scroll.GetVScrollBar();
		bar.ValueChanged += (_) => CheckLoadMore();
		bar.Changed += CheckLoadMore;

		LoadNextPage();
	}

	private void UpdateColumns()
	{
		int columns = Mathf.Max(MinColumns, Mathf.FloorToInt((Size.X + Spacing) / (MinCardWidth + Spacing)));
		float cardWidth = Mathf.Floor((Size.X - (columns - 1) * Spacing) / columns);

		if (columns == Columns && Mathf.IsEqualApprox(cardWidth, _cardWidth))
		{
			return;
		}

		Columns = columns;
		_cardWidth = cardWidth;
		foreach (Node child in GetChildren())
		{
			PlaceCard.SetCardWidth((Control)child, _cardWidth);
		}
	}

	private void CheckLoadMore()
	{
		if (_loading || !_hasMorePages || Time.GetTicksMsec() < _retryAt)
		{
			return;
		}

		VScrollBar bar = _scroll.GetVScrollBar();
		if (bar.Value + bar.Page >= bar.MaxValue - LoadMoreDistance)
		{
			LoadNextPage();
		}
	}

	private async void LoadNextPage()
	{
		_loading = true;
		int page = _page + 1;
		AddSkeletons(page == 1 ? SkeletonCount : Columns);

		try
		{
			APIWorldsRoot root = page == 1 ? await WorldsCache.GetWorlds() : await PolyAPI.GetWorlds(page);
			RemoveSkeletons();

			_page = page;
			_hasMorePages = root.Meta.NextPageURL != null && root.Data.Length > 0;

			int index = 0;
			foreach (APIWorldsData item in root.Data)
			{
				if (item.IsLegacy || !_shownIDs.Add(item.Id))
				{
					continue;
				}

				PlaceCard card = PlaceCard.Create(item);
				AddChild(card);
				PlaceCard.SetCardWidth(card, _cardWidth);
				card.PopIn(index++);
			}
		}
		catch (Exception ex)
		{
			RemoveSkeletons();
			PT.PrintErr(ex);
			_retryAt = Time.GetTicksMsec() + RetryDelayMsec;

			if (page == 1)
			{
				if (OS.IsDebugBuild())
				{
					OS.Alert(ex.ToString(), "Error loading games");
				}
				else
				{
					OS.Alert("Something went wrong, please try again.", "Error");
				}
			}
		}

		_loading = false;
	}

	private void AddSkeletons(int count)
	{
		for (int i = 0; i < count; i++)
		{
			Control skeleton = PlaceCard.CreateSkeleton();
			AddChild(skeleton);
			PlaceCard.SetCardWidth(skeleton, _cardWidth);
			_skeletons.Add(skeleton);
		}
	}

	private void RemoveSkeletons()
	{
		foreach (Control skeleton in _skeletons)
		{
			RemoveChild(skeleton);
			skeleton.QueueFree();
		}

		_skeletons.Clear();
	}
}
