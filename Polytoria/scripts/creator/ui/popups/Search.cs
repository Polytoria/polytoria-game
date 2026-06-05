using Godot;
using Polytoria.Creator.UI;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Search : Panel
{
	[Export] public LineEdit? searchBar;
	[Export] public PackedScene? searchResult;
	[Export] public VBoxContainer? searchResultsContainer;
	[Export] public LoadingGuy? loadingGuy;

	private string _searchQuery = "";
	private string _classFilter = ""; // class:<class> e.g. class:InteractionPrompt
	private string _typeFilter = ""; // type:Object or type:File

	private SearchType _searchType = SearchType.All;
	private enum SearchType
	{
		All,
		Primary,
		Location,
		Content,
	}
	private bool _loading = false;

	public bool Loading
	{
		get => _loading;
		set
		{
			_loading = value;
			loadingGuy?.Visible = _loading;
		}
	}

	private void OnSearchQueryUpdate()
	{
		searchBar.Text = _searchQuery;
	}

	public class SearchResult
	{
		public int Matches = 0;
		public string Primary = null!;
		public string Location = null!;
	}


	public class InstanceSearchResult : SearchResult
	{
		public Instance ResultInstance = null!;
		public string Type = null!;
	}

	public class FileSearchResult : SearchResult
	{
		public string Path = null!;
		public string? Content;
		public bool IsText = false;
	}

	public override void _Ready() {
		searchBar.TextChanged += (_) => {
			_searchQuery = searchBar.Text;
			searchResults = [];
			UpdateResults();
			ProcessSearch();
		};
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("search") | (Visible && @event.IsActionPressed("toggle_menu")))
		{
			Visible = !Visible;
			_searchQuery = "";
			if (Visible)
			{
				OnSearchQueryUpdate();
				searchBar.GrabFocus();
				GetSearchCandidates();
			}
			GetViewport().SetInputAsHandled();
		}
	}

	private void NavigateInstance(Instance instance, string prefix)
	{

		InstanceSearchResult result = new();
		result.Primary = instance.Name;
		result.Type = instance.ClassName;
		result.Location = prefix.Length > 0 ? prefix + "/" + instance.Name : instance.Name;
		result.ResultInstance = instance;
		searchCandidates.Add(result);
		var children = instance.GetChildren();
		foreach (var child in children)
		{
			NavigateInstance(child, result.Location);
		}
	}

	private List<SearchResult> searchCandidates = [];
	private void GetSearchCandidates()
	{
		searchCandidates = [];
		var worlds = Tabs.Singleton.GetAllOpenWorlds();
		foreach (var world in worlds)
		{
			NavigateInstance(world.World, Tabs.Singleton.WorldContainerToTabTitle(world));
		}
		// PrintSearchCandidates();
	}

	private void PrintSearchCandidates()
	{
		PT.Print("got candidates");
		foreach (var cand in searchCandidates)
		{
			PT.Print($"{cand.Primary} ({cand.Location})");
		}
	}

	private void ProcessSearch()
	{
		Loading = true;
		_classFilter = "";
		_typeFilter = "";
		if (_searchQuery.Length == 0)
		{
			Loading = false;
			return;
		}
		switch (_searchQuery[0].ToString())
		{
			case "$":
				_searchType = SearchType.Primary;
				break;
			case "!":
				_searchType = SearchType.Location;
				break;
			case "%":
				_searchType = SearchType.Content;
				break;
			default:
				_searchType = SearchType.All;
				break;
		}
		List<string> query = [.. _searchQuery.Split(" ")];
		string finalQuery = "";
		foreach (var queryPart in query)
		{
			if (queryPart.Contains(":"))
			{
				List<string> parts = [.. queryPart.Split(":")];
				if (parts[0].ToLower() == "class")
				{
					_classFilter = parts[1];
				}
				else if (parts[0].ToLower() == "type")
				{
					_typeFilter = parts[1];
				}
				else
				{
					finalQuery = finalQuery.Length == 0 ? queryPart : finalQuery + " " + queryPart;
				}
			}
			else
			{
				finalQuery = finalQuery.Length == 0 ? queryPart : finalQuery + " " + queryPart;
			}
		}
		CalculateSearchResults(finalQuery.ToLower());
	}

	private List<SearchResult> searchResults = [];
	private void CalculateSearchResults(string query)
	{
		searchResults = [];
		List<SearchResult> unrankedResults = [];
		foreach (var cand in searchCandidates)
		{
			if (_typeFilter != "")
			{
				if (
					(_typeFilter.ToLower() == "file" && cand is FileSearchResult) ||
					(_typeFilter.ToLower() == "instance" && cand is InstanceSearchResult)
					)
				{
					unrankedResults.Add(cand);
				}
			}
			else
			{
				unrankedResults.Add(cand);
			}
		}
		foreach (var result in unrankedResults) {
			result.Matches = 0;
		}
		foreach (var result in unrankedResults)
		{
			if (_searchType == SearchType.Primary || _searchType == SearchType.All)
			{
				if (result.Primary.ToLower().Contains(query))
				{
					result.Matches++;
				}
			}
			if (_searchType == SearchType.Location || _searchType == SearchType.All)
			{
				if (result.Location.ToLower().Contains(query))
				{
					result.Matches++;
				}
			}
			if (
				(_searchType == SearchType.Content || _searchType == SearchType.All) &&
				result is FileSearchResult
				)
			{
				FileSearchResult resultFile = (FileSearchResult)result;
				if (resultFile.IsText && resultFile.Content != null && resultFile.Content.ToLower().Contains(query))
				{
					result.Matches++;
				}
			}
		}
		searchResults = unrankedResults.OrderByDescending(res => res.Matches).Where(res => res.Matches > 0).ToList();
		UpdateResults();
		Loading = false;
	}

	private void UpdateResults()
	{
		var children = searchResultsContainer.GetChildren();
		foreach (var child in children)
		{
			child.QueueFree();
		}
		foreach (var result in searchResults) {
			var resultNode = searchResult.Instantiate();
			resultNode.GetNode<Label>("HBoxContainer/VBoxContainer/Name").Text = result.Primary + $" ({result.Matches})";
			resultNode.GetNode<Label>("HBoxContainer/VBoxContainer/Location").Text = result.Location;
			searchResultsContainer.AddChild(resultNode);
		}
	}
}
