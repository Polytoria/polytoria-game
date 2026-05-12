// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Settings;
using Polytoria.Creator.UI.TextEditor;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Creator.UI;

public sealed partial class Tabs : TabContainer
{
	private readonly Dictionary<string, TextEditorContainer> _openedScripts = [];

	public static Tabs Singleton { get; private set; } = null!;
	public Tabs()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		TabBar bar = GetTabBar();
		bar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways;
		bar.TabClosePressed += async idx =>
		{
			Control control = GetTabControl((int)idx);

			if (control is WorldContainer || control is TextEditorContainer)
			{
				if (!(control is TextEditorContainer txt && txt.EditorRoot.Saved))
				{
					if (!await CreatorService.Interface.PromptConfirmation("Are you sure you want to close this tab? Any unsaved changes will be lost.", dismissKey: CreatorSettingKeys.Popups.CloseTabWarning)) return;
				}
			}

			if (control is WorldContainer g)
			{
				g.World.ForceDelete();
			}
			else if (control is TextEditorContainer tec)
			{
				_openedScripts.Remove(tec.TargetFilePathAbsolute);
			}
			control.QueueFree();
		};

		TabChanged += idx =>
		{
			World? game = null;

			if (idx != -1)
			{
				Control control = GetTabControl((int)idx);

				if (control is WorldContainer gameContainer)
				{
					game = gameContainer.World;
				}
				if (control is TextEditorContainer textedit)
				{
					game = World.Current;
				}
			}

			World.Current = game;
		};
	}

	public void CloseTabsOfSession(CreatorSession session)
	{
		foreach ((string k, TextEditorContainer c) in _openedScripts)
		{
			if (c.TargetSession == session)
			{
				_openedScripts.Remove(k);
				c.QueueFree();
			}
		}
	}

	public void SetTabTitle(Control c, string to)
	{
		SetTabTitle(GetTabIdxFromControl(c), to);
	}

	public void Insert(TabData other, string? title = null)
	{
		Node container;
		string icon;

		if (other is GameTab gt)
		{
			container = new WorldContainer(gt.World);
			icon = "World";

			void deleted()
			{
				gt.World.Deleted -= deleted;
				if (IsInstanceValid(container))
					container.QueueFree();
			}

			gt.World.Deleted += deleted;
		}
		else if (other is TextEditorTab txt)
		{
			string fullPath = txt.Session.GlobalizePath(txt.TargetPath);
			if (_openedScripts.TryGetValue(fullPath, out TextEditorContainer? existingTec))
			{
				CurrentTab = GetTabIdxFromControl(existingTec);
				return;
			}
			ScriptTypeEnum st = CreatorService.GetScriptTypeFromPath(txt.TargetPath);
			TextEditorContainer tec = new(txt.TargetPath, fullPath, txt.Session) { OriginTabName = txt.Title ?? "" };
			container = tec;
			if (st == ScriptTypeEnum.Module)
			{
				icon = "ModuleScript";
			}
			else if (st == ScriptTypeEnum.Server)
			{
				icon = "ServerScript";
			}
			else if (st == ScriptTypeEnum.Client)
			{
				icon = "ClientScript";
			}
			else
			{
				icon = "Script";
			}
			_openedScripts[fullPath] = tec;
		}
		else
		{
			throw new NotImplementedException();
		}

		AddChild(container, true);
		int idx = GetTabCount() - 1;

		SetTabTitle(idx, title ?? other.Title);
		SetTabIcon(idx, Globals.LoadIcon(icon));

		CurrentTab = idx;
	}

	public class TextEditorTab : TabData
	{
		public string TargetPath = null!;
		public CreatorSession Session = null!;
	}

	public class GameTab : TabData
	{
		public World World = null!;
	}

	public class TabData
	{
		public string Title = "Tab";
	}
}
