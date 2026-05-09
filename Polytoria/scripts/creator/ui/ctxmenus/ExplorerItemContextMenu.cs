// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator.Managers;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Interfaces;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace Polytoria.Creator.UI;

public partial class ExplorerItemContextMenu : ContextMenu
{
	public required List<Instance> Targets;
	public Instance? Target;

	private string baseUri = "https://v2docs.polytoria.com/api/types/";

	// docmap
	private Dictionary<string, HashSet<string>> gameData;

	[RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed")]
	[RequiresDynamicCode("JSON deserialization creates dynamic code at runtime which is incompatible with NativeAOT")]
	public override void _Ready()
	{
		if (gameData == null)
		{
			try
			{
				string jsonText = File.ReadAllText("assets/docs/doc_map.json");
				gameData = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(jsonText);
			}
			catch (Exception e)
			{
				GD.Print("JSON Load Failed: " + e.Message);
			}
		}

		bool isSingle = Targets.Count == 1;
		if (isSingle)
		{
			Target = Targets[0];
			AddIconItem("plus", "Add Child", 1);
			AddIconItem("script", "Add Script", 2);
			AddSeparator();
			if (Target.LinkedModel != null)
			{
				if (Target.EditableChildren)
				{
					AddIconItem("edit", "Close Model", 41);
				}
				else
				{
					AddIconItem("edit", "Edit Model", 41);
				}
				AddIconItem("save", "Save Model", 42);
				AddIconItem("link-off", "Detach Model", 43);
				AddSeparator();
			}
		}
		AddIconItem("cut", "Cut", 20);
		AddIconItem("copy", "Copy", 21);
		AddIconItem("clipboard", "Paste", 22);
		AddIconItem("duplicate", "Duplicate", 23);
		AddIconItem("select-all", "Select Children", 25);
		AddSeparator();
		AddIconItem("group", "Group", 31);
		if (Target is IGroup)
		{
			AddIconItem("ungroup", "Ungroup", 32);
		}

		// TODO: Implement Model publish
		//AddIconItem("publish", "Publish", 39);

		if (isSingle)
		{
			AddIconItem("route", "Copy Lua Path", 51);
			AddIconItem("book", "Open Documentation", 59);

		}
		AddSeparator();
		AddIconItem("lock", "Lock/Unlock", 61);
		if (Target is ServerScript)
		{
			AddSeparator();
			AddIconItem("addon", "Install as addon", 71);
		}
		if (!Targets[0].GetType().IsDefined(typeof(StaticAttribute), false))
		{
			AddSeparator();
			AddIconItem("trash", "Delete", 101);
		}

		IdPressed += OnIdPressed;
	}

	private async void OnIdPressed(long id)
	{
		Instance[] targets = [.. Targets];
		CreatorContextService context = targets[0].Root.CreatorContext;

		switch (id)
		{
			case 1: // Add child
				{
					InsertMenuPopup menu = CreatorService.Interface.OpenInsertMenu(Target);
					break;
				}
			case 2: // Add script
				{
					CreatorService.Interface.PromptCreateScript(Target);
					break;
				}
			case 20: // Cut
				{
					await CreatorService.Clipboard.SetClipboard(targets);
					context.History.DeleteInstances(targets);
					break;
				}
			case 21: // Copy
				{
					await CreatorService.Clipboard.SetClipboard(targets);
					break;
				}
			case 22: // Paste
				{
					await CreatorService.Clipboard.PasteClipboard(true);
					break;
				}
			case 23: // Duplicate
				{
					context.History.DuplicateInstances(targets);
					break;
				}
			case 25: // Select Children
				{
					context.Selections.DeselectAll();
					foreach (Instance item in targets)
					{
						context.Selections.SelectChild(item);
					}
					break;
				}
			case 31: // Group
				{
					context.History.GroupInstances(targets);
					break;
				}
			case 32: // Ungroup
				{
					context.History.UngroupInstances(targets);
					break;
				}
			case 39: // Publish
				{
					CreatorService.Interface.OpenPublish(Target!);
					break;
				}
			case 41: // Edit Model
				{
					if (Target != null)
					{
						if (Target.EditableChildren)
						{
							if (!await CreatorService.Interface.PromptConfirmation("Closing this model will discard any unsaved changes.", dismissKey: "Popups.CloseModelWarning")) return;
						}
						Target?.EditableChildren = !Target.EditableChildren;
					}
					break;
				}
			case 42: // Save Model
				{
					Target?.SaveModel();
					break;
				}
			case 43: // Detach Model
				{
					Target?.DetachModel();
					break;
				}
			case 51: // Copy Lua Path
				{
					DisplayServer.ClipboardSet(Target!.LuaPath);
					break;
				}
			case 59: // Open Documentation
				{
					// Setup
					string item = Target!.LegacyName;
					var match = gameData.FirstOrDefault(pair => pair.Value.Contains(item));
					if (match.Key is null)
					{
						// Oh no :(
						GD.Print($"Couldn't find a category for item: {item}");
						break;
					}
					string category = match.Key;

					// Figuring out the link
					UriBuilder linkBuilder = new UriBuilder(baseUri);
					linkBuilder.Path += category;
					linkBuilder.Path += "/";
					linkBuilder.Path += item;
					string finalUri = Convert.ToString(linkBuilder.Uri);

					// Tries to open the Uri
					Error result = OS.ShellOpen(finalUri);
					if (result != Error.Ok)
					{
						// Oh no :(
						GD.Print($"Something went wrong trying to open the site: {finalUri}");
						break;
					}

					// Everything good
					GD.Print($"Successfully redirected to the site: {finalUri}");
					break;
				}
			case 61: // Lock/Unlock
				{
					List<Dynamic> dyns = [];
					foreach (Instance item in targets)
					{
						if (item is Dynamic dyn)
						{
							dyns.Add(dyn);
						}
					}
					context.History.ToggleLockedDynamics([.. dyns]);
					break;
				}
			case 71: // Install as addon
				{
					if (Target is ServerScript s)
					{
						await AddonsManager.InstallAddonFromScript(s);
					}
					break;
				}
			case 101: // Delete
				{
					context.History.DeleteInstances(targets);
					break;
				}
		}
	}
}
