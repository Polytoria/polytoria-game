// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using System.Collections.Generic;

namespace Polytoria.Mobile.UI;

public partial class WorldRow : VBoxContainer
{
	private const int SkeletonCount = 4;

	[Export] public Texture2D? HeaderTexture;

	[Export] private TextureRect _header = null!;
	[Export] private ScrollContainer _scroll = null!;
	[Export] private Container _cardContainer = null!;

	public override void _Ready()
	{
		_header.Texture = HeaderTexture;
	}

	public void ShowLoading()
	{
		ClearCards();
		for (int i = 0; i < SkeletonCount; i++)
		{
			_cardContainer.AddChild(PlaceCard.CreateSkeleton());
		}
	}

	public void SetWorlds(IEnumerable<APIWorldsData> worlds)
	{
		ClearCards();
		_scroll.ScrollHorizontal = 0;

		int index = 0;
		foreach (APIWorldsData world in worlds)
		{
			PlaceCard card = PlaceCard.Create(world);
			_cardContainer.AddChild(card);
			card.PopIn(index++);
		}

		Visible = index > 0;
	}

	private void ClearCards()
	{
		foreach (Node child in _cardContainer.GetChildren())
		{
			_cardContainer.RemoveChild(child);
			child.QueueFree();
		}
	}
}
