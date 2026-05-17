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
using System.Linq;
using System.Threading.Tasks;

namespace Polytoria.Creator.UI;

public sealed partial class Tabs : Control
{
	private Control? _currentControl;

	[Export]
	public Control? CurrentControl
	{
		get => _currentControl;
		private set
		{
			if (_currentControl == value) return;
			_currentControl?.Visible = false;

			_currentControl = value;
			if (value == null) return;

			if (!_controlToIdx.TryGetValue(value, out var idx))
			{
				_tabsContainer.AddChild(value, true);

				idx = _tabBar.TabCount - 1;
				_orderedControls.Add(value);
				_controlToIdx[value] = idx;
			}
			value.Visible = true;
			_surpressTabChanged = true;
			_tabBar.CurrentTab = idx;
			_surpressTabChanged = false;
			UpdateTabBar();

			if (value is WorldContainer gameContainer)
			{
				World.Current = gameContainer.World;
			}
			if (value is TextEditorContainer tec && World.Current != null && World.Current.LinkedSession != tec.TargetSession)
			{
				World.Current = tec.TargetSession.OpenedWorlds[0];
			}
		}
	}

	private bool _surpressTabChanged;

	private int _selectedIdx;

	private readonly List<Control> _orderedControls = [];
	private readonly Dictionary<Control, int> _controlToIdx = [];
	private readonly Dictionary<string, Control> _openedFiles = [];

	private Control _tabsClip = null!;
	private TabBar _tabBar = null!;
	private PanelContainer _tabsContainer = null!;

	private Button _leftButton = null!, _rightButton = null!;
	private bool _scrollLeft, _scrollRight;
	private int _maxScroll;

	private const int _scrollSidePadding = 2;

	public static Tabs Singleton { get; private set; } = null!;
	public Tabs()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		_tabsClip = GetNode<Control>("Bar/TabsClip");
		_tabBar = GetNode<TabBar>("Bar/TabsClip/TabBar");
		_tabsContainer = GetNode<PanelContainer>("Container");

		_leftButton = GetNode<Button>("Bar/TabsClip/LeftButton");
		_rightButton = GetNode<Button>("Bar/TabsClip/RightButton");

		_tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways;

		_tabsClip.Resized += UpdateTabBar;

		_tabBar.TabChanged += idx =>
		{
			if (_surpressTabChanged) return;
			if ((int)idx < 0 || (int)idx >= _orderedControls.Count) return;
			CurrentControl = _orderedControls[(int)idx];
		};

		_tabBar.TabSelected += idx => _selectedIdx = (int)idx;

		_tabBar.ActiveTabRearranged += newIdx =>
		{
			var control = _orderedControls[_selectedIdx];
			_orderedControls.RemoveAt(_selectedIdx);
			_orderedControls.Insert((int)newIdx, control);
			RebuildLookup();
			_selectedIdx = -1;
		};

		_tabBar.TabClosePressed += async idx => await Remove(_orderedControls[(int)idx]);

		_leftButton.ButtonDown += () => _scrollLeft = true;
		_leftButton.ButtonUp += () => _scrollLeft = false;
		_rightButton.ButtonDown += () => _scrollRight = true;
		_rightButton.ButtonUp += () => _scrollRight = false;
	}

	public void SetTabTitle(Control c, string to)
	{
		_tabBar.SetTabTitle(_controlToIdx[c], to);
	}

	public void Insert(TabData other, string? title = null)
	{
		Control container;
		string icon;

		if (other is GameTab gt)
		{
			container = new WorldContainer(gt.World);
			icon = "World";

			void deleted()
			{
				gt.World.Deleted -= deleted;
				if (IsInstanceValid(container))
				{
					container.QueueFree();
				}
			}
			gt.World.Deleted += deleted;
		}
		else if (other is TextEditorTab txt)
		{
			string fullPath = txt.Session.GlobalizePath(txt.TargetPath);
			if (_openedFiles.TryGetValue(fullPath, out Control? existing))
			{
				CurrentControl = existing;
				return;
			}
			TextEditorContainer tec = new(txt.TargetPath, fullPath, txt.Session) { OriginTabName = txt.Title ?? "" };
			container = tec;
			ScriptTypeEnum st = CreatorService.GetScriptTypeFromPath(txt.TargetPath);
			icon = st switch
			{
				ScriptTypeEnum.Module => "ModuleScript",
				ScriptTypeEnum.Server => "ServerScript",
				ScriptTypeEnum.Client => "ClientScript",
				_ => "Script",
			};
			_openedFiles[fullPath] = tec;
		}
		else
		{
			throw new NotImplementedException();
		}

		_tabBar.AddTab(title ?? other.Title, Globals.LoadIcon(icon));
		CurrentControl = container;
	}

	private async Task Remove(Control control, bool isBulkOp = false)
	{
		if (control is WorldContainer || control is TextEditorContainer)
		{
			if (!(control is TextEditorContainer txt && txt.EditorRoot.Saved))
			{
				if (!await CreatorService.Interface.PromptConfirmation("Are you sure you want to close this tab? Any unsaved changes will be lost.", dismissKey: CreatorSettingKeys.Popups.CloseTabWarning)) return;
			}

			if (control is WorldContainer g)
			{
				g.World.ForceDelete();
			}
			if (control is TextEditorContainer tec)
			{
				_openedFiles.Remove(tec.TargetFilePathAbsolute);
			}
		}

		int idx = _controlToIdx[control];
		_orderedControls.RemoveAt(idx);
		_tabBar.RemoveTab(idx);

		if (!isBulkOp)
		{
			RebuildLookup();
			if (_tabBar.TabCount > 0 && control == CurrentControl)
				CurrentControl = _orderedControls[Mathf.Clamp(idx, 0, _tabBar.TabCount - 1)];
		}

		control.QueueFree();
		if (_tabBar.TabCount == 0)
		{
			_currentControl = null;
			World.Current = null;
		}
		UpdateTabBar();
	}

	private void RebuildLookup()
	{
		_controlToIdx.Clear();
		for (int i = 0; i < _tabBar.TabCount; i++)
			_controlToIdx[_orderedControls[i]] = i;
	}

	public void CloseTabsOfSession(CreatorSession session)
	{
		var sessionTabs = _orderedControls
			.Where(c => c is TextEditorContainer tec && tec.TargetSession == session)
			.Select(c => _controlToIdx[c])
			.OrderByDescending(i => i)
			.ToList();

		foreach (int idx in sessionTabs)
			_ = Remove(_orderedControls[idx], isBulkOp: true);

		if (_tabBar.TabCount > 0)
		{
			RebuildLookup();
			if (CurrentControl == null)
			{
				var lowestClosed = sessionTabs[^1];
				CurrentControl = _orderedControls[Mathf.Clamp(lowestClosed, 0, _tabBar.TabCount - 1)];
			}
		}
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

	public override void _Process(double delta)
	{
		if (_scrollLeft) ScrollTabBar((float)(900 * delta));
		if (_scrollRight) ScrollTabBar((float)(-900 * delta));
	}

	private void ScrollTabBar(float delta)
	{
		_tabBar.Position = new Vector2(_tabBar.Position.X + delta, _tabBar.Position.Y);
		ClampBarScroll();
	}

	private int GetMaxScroll()
	{
		_tabBar.Size = new Vector2(0, _tabBar.Size.Y);
		return (int)-Mathf.Max(_tabBar.Size.X - _tabsClip.Size.X + _scrollSidePadding, -_scrollSidePadding);
	}

	private void UpdateTabBar()
	{
		_maxScroll = GetMaxScroll();
		ClampBarScroll();
	}

	private void ClampBarScroll()
	{
		var pos = _tabBar.Position;
		pos.X = Mathf.Clamp(pos.X, _maxScroll, _scrollSidePadding);
		_tabBar.Position = pos;
		UpdateScrollButtons();
	}

	private void UpdateScrollButtons()
	{
		if (_tabBar.Position.X == _maxScroll)
		{
			_rightButton.Visible = false;
			_scrollRight = false;
		}
		else if (!_rightButton.Visible) _rightButton.Visible = true;

		if (_tabBar.Position.X == _scrollSidePadding)
		{
			_leftButton.Visible = false;
			_scrollLeft = false;
		}
		else if (!_leftButton.Visible) _leftButton.Visible = true;
	}
}
