using Godot;
using Polytoria.Creator.UI;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public partial class Search : Panel
{
	[Export] public LineEdit? searchBar;
	[Export] public PackedScene? searchResult;
	[Export] public VBoxContainer? searchResultsContainer;
	[Export] public Control? loadingSpinner;

	[Export] public Label? statusText;

	private int searchResultIndex = 0;

	private string _searchQuery = "";
	private string _classFilter = ""; // class:<class> e.g. class:InteractionPrompt
	private string _typeFilter = ""; // type:Instance or type:File

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
			loadingSpinner?.Visible = _loading;
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

	public override void _Ready()
	{
		searchBar.TextChanged += (_) =>
		{
			statusText.Visible = false;
			_searchQuery = searchBar.Text;
			searchResults = [];
			UpdateResults();
			ProcessSearch();
			if (searchResultIndex >= searchResults.Count)
			{
				searchResultIndex = Math.Max(0, searchResults.Count - 1);
				SelectResultBasedOnIndex();
			}
			if (searchResults.Count == 0 && searchBar.Text.Length > 0)
			{
				statusText?.Text = "No Results Found";
				statusText?.Visible = true;
			}
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
				searchResultIndex = -1;
				OnSearchQueryUpdate();
				searchBar.GrabFocus();
				GetSearchCandidates();
			}
			GetViewport().SetInputAsHandled();
		}
		if (!Visible)
		{
			return;
		}
		if (@event.IsActionPressed("ui_down"))
		{
			searchResultIndex++;
			if (searchResultIndex >= searchResults.Count)
			{
				searchResultIndex = 0;
			}
			SelectResultBasedOnIndex();
		}
		if (@event.IsActionPressed("ui_up"))
		{
			searchResultIndex--;
			if (searchResultIndex < 0)
			{
				searchResultIndex = searchResults.Count - 1;
			}
			SelectResultBasedOnIndex();
		}
	}

	private void SelectResultBasedOnIndex()
	{
		var child = searchResultsContainer.GetChild(searchResultIndex);
		if (child is PanelContainer pc)
		{
			pc.GetNode<Button>("Button").GrabFocus();
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

	private string[] _textBasedFiles = ["md", "txt", "ptproj", "json", "xml", "lua", "luau", "cs"];
	private bool IsTextBasedFile(string path)
	{ // file extension checking as its cheap and fast :3
		string? ext = Path.GetExtension(path);
		if (ext == null)
		{
			return false;
		}
		if (ext.StartsWith("."))
		{ // sometimes does? idk
			ext = ext[1..];
		}
		return _textBasedFiles.Contains(ext);
	}

	private async Task NavigateDirectory(string rootPath, string path, int maxSize)
	{
		Loading = true;
		await Task.Run(() =>
		{
			string[] files = Directory.GetFiles(path);
			foreach (var file in files)
			{
				FileInfo info = new FileInfo(file);
				FileSearchResult result = new();
				result.Primary = info.Name;
				result.Location = Path.Join(Path.GetRelativePath(rootPath, path), result.Primary);
				result.IsText = IsTextBasedFile(file);
				if (result.IsText && info.Length < maxSize)
				{
					var text = File.ReadAllText(file);
					result.Content = text;
				}
				searchCandidates.Add(result);
			}
			string[] subdirectories = Directory.GetDirectories(path);
			foreach (var dir in subdirectories)
			{
				bool? isHidden = Path.GetFileName(dir)?.StartsWith(".");
				if (isHidden == true)
				{
					continue; // mainly to avoid .git and .poly dirs
				}
				NavigateDirectory(rootPath, dir, maxSize);
			}
		});
		Loading = false;
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
		var path = FileBrowser.CurrentSession?.ProjectFolderPath;
		if (path == null)
		{ // skip loading file assets
			return;
		}
		NavigateDirectory(path, path, 1048576); // 1mb
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
		if (_searchType != SearchType.All)
		{
			_searchQuery = _searchQuery[1..];
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
			if (_classFilter == "" && _typeFilter == "")
			{
				unrankedResults.Add(cand);
				continue;
			}
			if (_typeFilter != "")
			{
				if (
					(_typeFilter.ToLower() == "file" && _classFilter == "" && cand is FileSearchResult) ||
					(_typeFilter.ToLower() == "instance" && cand is InstanceSearchResult)
					)
				{
					unrankedResults.Add(cand);
				}
			}

			if (_classFilter != "")
			{
				if (cand is InstanceSearchResult instanceCand)
				{
					if (_classFilter.ToLower() == instanceCand.ResultInstance.ClassName.ToLower())
					{
						unrankedResults.Add(cand);
					}
				}
			}
		}
		foreach (var result in unrankedResults)
		{
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
				result is FileSearchResult resultFile
				)
			{
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
		foreach (var result in searchResults)
		{
			var resultNode = searchResult.Instantiate();
			resultNode.GetNode<Button>("Button").Pressed += () =>
			{
				if (result is FileSearchResult fileResult)
				{
					CreatorService.OpenFile(result.Location);
				}
				else if (result is InstanceSearchResult instanceResult)
				{
					Explorer.CurrentRoot?.CreatorContext.Selections.DeselectAll();
					Explorer.Select(instanceResult.ResultInstance);
				}
				Visible = false;
			};
			resultNode.GetNode<Label>("HBoxContainer/Name").Text = result.Primary;
			resultNode.GetNode<Label>("HBoxContainer/Location").Text = result.Location;
			searchResultsContainer.AddChild(resultNode);
		}
	}
}
