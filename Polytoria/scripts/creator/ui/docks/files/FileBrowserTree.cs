// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Settings;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;

namespace Polytoria.Creator.UI;

public partial class FileBrowserTree : Tree
{
	public CreatorSession Session = null!;
	public Dictionary<string, TreeItem> FileToItem = [];
	public Dictionary<TreeItem, string> ItemToFile = [];

	public readonly HashSet<string> AutoSelects = [];
	public FileItemContextMenu? ItemContextMenu;
	public Dictionary<string, TreeItem> SearchItems = [];

	public override void _Ready()
	{
		ItemActivated += OnItemActivated;
		ItemEdited += OnItemEdited;
		base._Ready();
	}

	public void Search(string query)
	{
		if (string.IsNullOrEmpty(query))
		{
			foreach (TreeItem item in SearchItems.Values)
			{
				item.Visible = true;
			}
			return;
		}

		foreach (TreeItem item in SearchItems.Values)
		{
			item.Visible = false;
		}

		bool isFirst = true;

		foreach ((string path, TreeItem item) in SearchItems)
		{
			if (path.Find(query, caseSensitive: false) != -1)
			{
				if (isFirst)
				{
					isFirst = false;
					item.Select(0);
				}
				item.Visible = true;
				item.SetCollapsedRecursive(false);

				// Show parents
				TreeItem? parent = item.GetParent();
				while (parent != null)
				{
					parent.Visible = true;
					parent = parent.GetParent();
				}
			}
		}
	}

	public override async void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
			{
				TreeItem clickedItem = GetItemAtPosition(mouseEvent.Position);
				if (clickedItem != null)
				{
					ItemContextMenu?.Close();

					// This is needed because selected files couldn't update beforehand
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

					string[] selectedFiles = GetSelectedFiles();
					ItemContextMenu = new() { Session = Session, Targets = selectedFiles };
					AddChild(ItemContextMenu);
					ItemContextMenu.PopupAtCursor();
				}
			}
		}
		if (@event.IsActionPressed("delete"))
		{
			AcceptEvent();
			CreatorService.Interface.PromptDeleteFiles(GetSelectedFiles());
		}
		base._GuiInput(@event);
	}

	private async void OnItemActivated()
	{
		TreeItem target = GetSelected();

		if (target == null)
		{
			return;
		}

		string clickedFile = ItemToFile[target];

		if (Session.GetFileAttributes(clickedFile) == FileAttributes.Directory)
		{
			// Expand folder
			target.Collapsed = !target.Collapsed;
		}
		else
		{
			CreatorService.OpenFile(clickedFile);
		}
	}

	private void OnItemEdited()
	{
		TreeItem target = GetEdited();
		string path = (string)target.GetMeta("path", "");

		Session.RenameFile(path, target.GetText(0));
	}

	public string[] GetSelectedFiles()
	{
		List<string> files = [];
		CollectSelected(GetRoot(), files);
		return [.. files];
	}

	private void CollectSelected(TreeItem? item, List<string> files)
	{
		while (item != null)
		{
			if (item.IsSelected(0) && ItemToFile.TryGetValue(item, out var v))
				files.Add(v);

			// Recurse into children
			CollectSelected(item.GetFirstChild(), files);
			item = item.GetNext();
		}
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		return new FileDragData()
		{
			Files = GetSelectedFiles()
		}.Serialize();
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		DropModeFlags = (int)DropModeFlagsEnum.OnItem;

		return true;
	}

	public override async void _DropData(Vector2 atPosition, Variant data)
	{
		TreeItem targetItem = GetItemAtPosition(atPosition);

		string target = ItemToFile[targetItem];

		DropModeFlags = (int)DropModeFlagsEnum.Disabled;

		if (target == null)
			return;

		IDragDataUnion? dragData = DragData.Deserialize(data);

		if (dragData is FileDragData fileDrag)
		{
			if (!await CreatorService.Interface.PromptConfirmation($"Move {fileDrag.Files.Length} file(s)?", dismissKey: CreatorSettingKeys.Popups.MoveFileConfirmation)) return;
			AutoSelects.Clear();
			foreach (string file in fileDrag.Files)
			{
				string? resultPath = Session.MoveFile(file, target);
				if (resultPath != null)
				{
					AutoSelects.Add(resultPath);
				}
			}
			Session.RescanFolder();
		}
		else if (dragData is InstanceDragData instanceDrag)
		{
			AutoSelects.Clear();
			foreach (Instance i in instanceDrag.Instances)
			{
				string createPath;

				// is folder
				if (Session.GetFileAttributes(target) == FileAttributes.Directory)
				{
					createPath = target.PathJoin(i.Name + "." + Globals.ModelFileExtension);
				}
				else
				{
					createPath = target;
				}

				if (Session.FileExists(createPath))
				{
					if (createPath.GetExtension() != Globals.ModelFileExtension)
					{
						CreatorService.Interface.PopupAlert("Cannot save to non model file");
						return;
					}
					bool confirm = await CreatorService.Interface.PromptConfirmation(createPath.GetFile() + " already exists, do you want to override it?", "Warning!");
					if (!confirm) return;
				}

				try
				{
					Session.SaveModel(i, createPath);
					AutoSelects.Add(createPath);
				}
				catch (Exception ex)
				{
					CreatorService.Interface.PopupAlert(ex.Message);
				}
			}
			Session.RescanFolder();
		}
		else
		{
			return;
		}
	}
}
