// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using Polytoria.Formats;
using Polytoria.Shared;
using System.IO;
using System.Linq;

namespace Polytoria.Creator.UI;

public partial class FileItemContextMenu : ContextMenu
{
	public CreatorSession Session = null!;

	public required string[] Targets;
	public string? Target;
	private PopupMenu? _newMenu = null;

	public override void _Ready()
	{
		bool isSingle = Targets.Length == 1;
		bool isNotLocked = true;

		// Scan for root folder
		foreach (string item in Targets)
		{
			if (item == Globals.ProjectMetaFileName || item == "")
			{
				isNotLocked = false;
				break;
			}
		}

		if (isSingle)
		{
			Target = Targets[0];
			string ext = Target.GetExtension();

			if (Globals.ScriptFileExtensions.Contains(ext) && Globals.IsInGDEditor)
			{
				AddIconItem("play", "Run (Developer Only)", 71);
			}

			if (ext == "poly")
			{
				AddIconItem("star", "Set as main world", 89);
				AddSeparator();
			}

			if (Session.GetFileAttributes(Target) == FileAttributes.Directory)
			{
				_newMenu = new();
				SetIconItem(_newMenu, "folder", "Folder", 1);
				SetIconItem(_newMenu, "script", "File", 2);
				SetIconItem(_newMenu, "script", "Script", 3);
				SetIconItem(_newMenu, "planet", "World", 4);
				_newMenu.IdPressed += OnNewMenuIdPressed;

				AddSubmenuNodeItem("New", _newMenu, 1);
				SetItemIcon(GetItemIndex(1), Globals.LoadUIIcon("plus"));
				SetItemIconMaxWidth(GetItemIndex(1), ItemIconSize);
				AddSeparator();
			}

			if (isNotLocked)
			{
				AddIconItem("edit", "Rename", 11);
				//AddIconItem("duplicate", "Duplicate", 12);
				AddSeparator();
			}

			if (ext == Globals.ModelFileExtension || ext == "poly")
			{
				AddCheckItem("Compressed", 81);
				SetItemChecked(GetItemIndex(81), PolyFormat.IsPolyFileCompressed(Session.GlobalizePath(Target)));
				AddSeparator();
			}

			AddIconItem("copy", "Copy Path", 31);
			AddIconItem("", "Copy Absolute Path", 32);
			AddIconItem("route", "Open in File manager", 39);
			AddSeparator();
		}

		if (!(Target != null && Target == Globals.ProjectMetaFileName))
		{
			AddIconItem("trash", "Delete", 61);
		}

		IdPressed += OnIdPressed;
	}

	private void OnNewMenuIdPressed(long id)
	{
		switch (id)
		{
			case 1: // New Folder
				CreatorService.Interface.PromptCreateFolder(Target!);
				break;
			case 2: // New File
				CreatorService.Interface.PromptCreateFile(atPath: Target);
				break;
			case 3: // New Script
				CreatorService.Interface.PromptCreateScript(atPath: Target);
				break;
			case 4: // New World
				CreatorService.Interface.PromptCreateWorld(Target!);
				break;
		}
	}

	private async void OnIdPressed(long id)
	{
		switch (id)
		{
			case 11: // Rename
				Session.FileBrowserTab.RenameSelected();
				break;
			case 31: // Copy Path
				DisplayServer.ClipboardSet(Target);
				break;
			case 32: // Copy Absoulete Path
				DisplayServer.ClipboardSet(Session.GlobalizePath(Target!));
				break;
			case 39: // Open in File manager
				OS.ShellShowInFileManager(Session.GlobalizePath(Target!));
				break;
			case 61: // Delete
				CreatorService.Interface.PromptDeleteFiles(Targets);
				break;
			case 71: // Run script
				if (!await CreatorService.Interface.PromptConfirmation("This script will have unrestricted access to your device. Are you sure you want to run it?")) return;
				Session.RunScript(Targets[0]);
				break;
			case 81: // Run script
				Session.ToggleCompressed(Targets[0]);
				break;
			case 89: // Set as main world
				Session.SetMainWorld(Targets[0]);
				break;
		}
	}
}
