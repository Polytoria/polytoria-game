// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;

namespace Polytoria.Creator.UI;

public sealed partial class Toolbox : Control
{
	private const string ToolboxCardPath = "res://scenes/creator/docks/toolbox/toolbox_card.tscn";

	[Export] private OptionButton _typeOption = null!;
	[Export] private OptionButton _filterOption = null!;
	[Export] private LineEdit _searchEdit = null!;
	[Export] private Control _resultsContainer = null!;
	[Export] private Button _pagNavPrev = null!;
	[Export] private Label _pagNavLabel = null!;
	[Export] private Button _pagNavNext = null!;
	[Export] private Control _loaderView = null!;
	[Export] private Control _noResultView = null!;

	public LibraryQueryTypeEnum QueryType = LibraryQueryTypeEnum.Model;
	public int CurrentPage = 1;
	public int MaxPage = 1;
	public string SearchQuery = "";
	public ToolboxCard? SoundPreviewingCard;

	public override void _Ready()
	{
		base._Ready();

		_searchEdit.TextSubmitted += (_) => OnSearch();
		_searchEdit.FocusExited += OnSearch;
		_typeOption.GetPopup().IdPressed += OnQueryType;

		_pagNavPrev.Pressed += OnPrevious;
		_pagNavNext.Pressed += OnNext;

		Refresh();
	}

	private void UpdateNavLabel()
	{
		_pagNavLabel.Text = $"Page {CurrentPage} of {MaxPage}";
	}

	private void OnNext()
	{
		CurrentPage++;
		if (CurrentPage > MaxPage)
		{
			CurrentPage = MaxPage;
		}
		Refresh();
	}

	private void OnPrevious()
	{
		CurrentPage--;
		if (CurrentPage < 1)
		{
			CurrentPage = 1;
		}
		Refresh();
	}

	private void OnSearch()
	{
		if (SearchQuery == _searchEdit.Text) return;
		SearchQuery = _searchEdit.Text;
		CurrentPage = 1;
		Refresh();
	}

	private void OnQueryType(long idx)
	{
		QueryType = idx switch
		{
			0 => LibraryQueryTypeEnum.Model,
			1 => LibraryQueryTypeEnum.Image,
			2 => LibraryQueryTypeEnum.Audio,
			3 => LibraryQueryTypeEnum.Mesh,
			4 => LibraryQueryTypeEnum.Addon,
			_ => LibraryQueryTypeEnum.Model
		};
		CurrentPage = 1;
		Refresh();
	}

	public void Refresh()
	{
		Clear();
		ListItems();
	}

	public void Clear()
	{
		foreach (Node item in _resultsContainer.GetChildren())
		{
			item.QueueFree();
		}
	}

	public async void ListItems()
	{
		_loaderView.Visible = true;
		_noResultView.Visible = false;
		PT.Print("Querying Toolbox data...");

		// Disable the buttons
		_pagNavNext.Disabled = true;
		_pagNavPrev.Disabled = true;

		APILibraryResponse res = await PolyAPI.GetLibrary(QueryType, CurrentPage, SearchQuery);
		MaxPage = res.Meta.LastPage;

		_pagNavPrev.Disabled = CurrentPage == 1;
		_pagNavNext.Disabled = CurrentPage >= MaxPage;

		PT.Print("Query Complete!");

		_loaderView.Visible = false;

		_noResultView.Visible = res.Data.Length == 0;

		foreach (APILibraryItem item in res.Data)
		{
			ToolboxCard card = Globals.CreateInstanceFromScene<ToolboxCard>(ToolboxCardPath);
			card.ItemData = item;
			card.ItemType = QueryType;
			card.ToolboxParent = this;
			_resultsContainer.AddChild(card);
		}

		UpdateNavLabel();
	}
}
