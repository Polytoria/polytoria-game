// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI.Splashes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using static Polytoria.Datamodel.Creator.CreatorAddons;

namespace Polytoria.Creator.UI;

public sealed partial class Menu : PanelContainer
{
	private sealed class MenuAddonSlotItem : MenuButtonItem
	{

	}

	private class MenuButtonItem : MenuItem
	{
		public string Text = "Unknown";
		public string? Icon;
		public Shortcut? KeyShortcut;
		public Action? Pressed;
		public bool RequireGameOpen = false;
		public int Id = 0;
		public int Index = 0;
	}

	private sealed class MenuSeperatorItem : MenuItem
	{
		public string? Text = null;
	}

	private abstract class MenuItem
	{

	}

	private sealed class MenuButtonMenus
	{
		public string Title = null!;
		public MenuButton Button = null!;
		public PopupMenu Popup = null!;
		public readonly Dictionary<int, MenuItem> IdToItem = [];
		public bool RequireGameOpen = false;
		public bool DevOnly = false;
	}

	private class AddonMenuData
	{
		public bool Visible = false;
		public int Index = -1;
		public int ItemId;
		public Dictionary<int, AddonToolItem> IndexToToolItem = [];
		public AddonObject AddonObject = null!;
	}

	public static Menu Singleton { get; private set; } = null!;

	private Control _menuButtons = null!;

	private readonly Dictionary<MenuButtonMenus, MenuItem[]> _menus = [];

	private readonly Dictionary<World, Dictionary<string, AddonMenuData>> _addonDataByRoot = [];
	private World? _currentRoot = null;
	private int _addonItemId = 0;

	private MenuButton _polyButton = null!;
	private PopupMenu _polyMenu = null!;
	private PopupMenu _addonSlotMenu = null!;

	public Menu()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		_menus.Add(
			new()
			{
				Title = "File",
			},
			[
				new MenuButtonItem() {
					Text = "New",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.N }
						]
					},
					Pressed = CreatorInterface.CreateNewWorld
				},
				new MenuButtonItem() {
					Text = "Open",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.O }
						]
					},
					Pressed = CreatorService.Interface.PromptOpenWorld
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Save",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.S }
						]
					},
					Pressed = () => {
						CreatorService.SaveCurrentFile();
					}
				},
				new MenuButtonItem() {
					Text = "Save As...",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.S }
						]
					},
					Pressed = CreatorService.SaveCurrentFileAs
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Publish",
					RequireGameOpen = true,
					Pressed = async () => {
						if (World.Current != null)
						{
							CreatorService.Interface.OpenPublish(World.Current);
						}
					}
				},
				new MenuButtonItem() {
					Text = "Exit",
					Pressed = () => {
						Globals.Singleton.Quit();
					}
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "Edit",
				RequireGameOpen = true,
			},
			[
				new MenuButtonItem() {
					Text = "Undo",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.Z }
						]
					},
					Pressed = CreatorService.Undo
				},
				new MenuButtonItem() {
					Text = "Redo",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.Z }
						]
					},
					Pressed = CreatorService.Redo
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Delete",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { Keycode = Key.Delete },
							new InputEventKey() { Keycode = Key.Backspace }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.DeleteSelected();
					}
				},
				new MenuButtonItem() {
					Text = "Duplicate",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.D }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.DuplicateSelected();
					}
				},
				new MenuButtonItem() {
					Text = "Toggle Locked",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.L }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.ToggleLockSelected();
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Select All",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.A }
						]
					},
					Pressed = () => {
						if (World.Current == null) return;
						World.Current.CreatorContext.Selections.SelectChild(World.Current.Environment);
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Input Manager",
					Pressed = CreatorService.Interface.OpenInputManager
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "Insert",
				RequireGameOpen = true,
			},
			[
				new MenuButtonItem() {
					Text = "New Instance",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { ShiftPressed = true, Keycode = Key.Space },
							new InputEventKey() { CtrlPressed = true, Keycode = Key.I },
						]
					},
					Pressed = () => {
						CreatorService.Interface.OpenInsertMenu();
					}
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "Model",
				RequireGameOpen = true,
			},
			[
				new MenuButtonItem() {
					Text = "Group",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.G }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.GroupSelected();
					}
				},
				new MenuButtonItem() {
					Text = "Ungroup",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.U }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.UngroupSelected();
					}
				},
				new MenuButtonItem() {
					Text = "Group Folder",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, AltPressed = true, Keycode = Key.G }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.GroupSelected(Datamodel.Creator.CreatorHistory.GroupAsEnum.Folder);
					}
				},
				new MenuButtonItem() {
					Text = "Group RigidBody",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.G }
						]
					},
					Pressed = () => {
						World.Current?.CreatorContext.Selections.GroupSelected(Datamodel.Creator.CreatorHistory.GroupAsEnum.RigidBody);
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Import",
					Pressed = () => {
						CreatorService.Interface.PromptImportModel();
					}
				},
				new MenuButtonItem() {
					Text = "Export",
					Pressed = () => {
						CreatorService.Interface.ExportSelectedModel();
					}
				}
			]
		);

		_menus.Add(
			new()
			{
				Title = "Tools",
			},
			[
				new MenuButtonItem() {
					Text = "Play Test",
					Icon = "play",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { Keycode = Key.F5 }
						]
					},
					Pressed = () => {
						CreatorService.Singleton.StartLocalTest();
					}
				},
				new MenuButtonItem() {
					Text = "Play Test Here",
					Icon = "camera",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { CtrlPressed = true, Keycode = Key.F5 }
						]
					},
					Pressed = () => {
						CreatorService.Singleton.StartLocalTest(true);
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Manage Addons",
					Pressed = CreatorService.Interface.PopupManageAddons
				},
				new MenuAddonSlotItem() {
					Text = "Addons",
					RequireGameOpen = true
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Migrate Coordinates",
					RequireGameOpen = true,
					Pressed = () => {
						CreatorService.MigrateCoordinates(World.Current!);
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Settings",
					Icon = "settings",
					Pressed = () => {
						CreatorService.Interface.OpenSettings();
					}
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "View",
			},
			[
				new MenuButtonItem() {
					Text = "Toggle Fullscreen",
					KeyShortcut = new()
					{
						Events = [
							new InputEventKey() { Keycode = Key.F11 }
						]
					},
					Pressed = () => {
						CreatorInterface.ToggleFullscreen();
					}
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "Help",
			},
			[
				new MenuButtonItem() {
					Text = "Copy System Info",
					Icon = "copy",
					Pressed = () => {
						DisplayServer.ClipboardSet($"System Name: {OS.GetName() + " " + OS.GetVersionAlias()}\nCPU: {OS.GetProcessorName()} cores: {OS.GetProcessorCount()}\nVideo Adapter: {OS.GetVideoAdapterDriverInfo().Join(", ")}");
					}
				},
				new MenuButtonItem() {
					Text = "Open Documentation",
					Pressed = () => {
						OS.ShellOpen("https://v2docs.polytoria.com/");
					}
				},
				new MenuSeperatorItem(),
				new MenuButtonItem() {
					Text = "Report a Bug",
					Pressed = () => {
						OS.ShellOpen("https://polytoria.com/forum/category/2");
					}
				},
			]
		);

		_menus.Add(
			new()
			{
				Title = "Dev",
				DevOnly = true
			},
			[
				new MenuButtonItem() {
					Text = "Pack Current Project",
					RequireGameOpen = true,
					Pressed = CreatorService.PackCurrentProject
				},
				new MenuButtonItem() {
					Text = "Link Device",
					Pressed = () => {
						CreatorService.Interface.OpenLinkDevicePrompt();
					}
				}
			]
		);

		_menuButtons = GetNode<Control>("Layout/MenuButtons");

		_polyButton = _menuButtons.GetNode<MenuButton>("Poly");
		_polyMenu = _polyButton.GetPopup();

		_polyMenu.IdPressed += OnPoly;

		foreach ((MenuButtonMenus mbtn, MenuItem[] items) in _menus)
		{
			if (mbtn.DevOnly && !Globals.IsInGDEditor) continue;
			MenuButton btnRoot = new()
			{
				Text = mbtn.Title,
				Flat = false,
				SwitchOnHover = true,
				FocusMode = FocusModeEnum.All,
			};
			PopupMenu menu = btnRoot.GetPopup();

			mbtn.Button = btnRoot;
			mbtn.Popup = menu;

			menu.IdPressed += (long idx) =>
			{
				if (mbtn.IdToItem[(int)idx] is MenuButtonItem btn)
				{
					btn.Pressed?.Invoke();
				}
			};

			int addedCount = 0;
			foreach (MenuItem item in items)
			{
				if (item is MenuButtonItem btnI)
				{
					int id = addedCount;
					mbtn.IdToItem[id] = item;
					if (btnI is MenuAddonSlotItem addonSlot)
					{
						_addonSlotMenu = new();
						menu.AddSubmenuNodeItem(btnI.Text, _addonSlotMenu, id);
					}
					else
					{
						menu.AddItem(btnI.Text, id);
					}

					int index = menu.GetItemIndex(id);

					btnI.Index = index;

					if (btnI.Icon != null)
					{
						menu.SetItemIcon(index, Globals.LoadUIIcon(btnI.Icon));
					}

					if (btnI.KeyShortcut != null)
					{
						// Setup Ctrl Auto remap for mac
						foreach (var ev in btnI.KeyShortcut.Events)
						{
							var ek = ev.As<InputEventKey>();
							if (ek.CtrlPressed)
							{
								ek.CommandOrControlAutoremap = true;
							}
						}
						menu.SetItemShortcut(index, btnI.KeyShortcut);
					}
					addedCount++;
				}
				else if (item is MenuSeperatorItem st)
				{
					menu.AddSeparator(st.Text ?? "", 1000);
					addedCount++;
				}
			}

			_menuButtons.AddChild(btnRoot);
		}

		foreach (Timer timer in FindChildren("*", "Timer", true, false).Cast<Timer>())
		{
			timer.IgnoreTimeScale = true;
			timer.WaitTime = 0.01;
		}

		SwitchTo(null);
	}

	public void UpdateAddonMenu(AddonObject obj)
	{
		// Get or create the dictionary for this root
		if (!_addonDataByRoot.TryGetValue(obj.Root, out var rootAddons))
		{
			rootAddons = [];
			_addonDataByRoot[obj.Root] = rootAddons;
		}

		if (!rootAddons.TryGetValue(obj.Identifier, out AddonMenuData? data))
		{
			rootAddons[obj.Identifier] = new() { AddonObject = obj };
			data = rootAddons[obj.Identifier];
		}

		data.AddonObject = obj;

		// Update the menu if is the current root
		if (obj.Root == _currentRoot)
		{
			UpdateAddonMenuInUI(obj, data);
		}
	}

	private void UpdateAddonMenuInUI(AddonObject obj, AddonMenuData data)
	{

		if (!data.Visible)
		{
			// Create new addon item
			_addonItemId++;

			_addonSlotMenu.AddSubmenuNodeItem(obj.AddonName, new(), _addonItemId);
			int index = _addonSlotMenu.GetItemIndex(_addonItemId);
			data.Index = index;
			data.Visible = true;
			data.ItemId = _addonItemId;

			PopupMenu mMenu = _addonSlotMenu.GetItemSubmenuNode(data.Index);
			mMenu.IndexPressed += ind =>
			{
				data.IndexToToolItem[(int)ind].Pressed.Invoke();
			};
		}

		_addonSlotMenu.SetItemText(data.Index, obj.AddonName);

		PopupMenu menu = _addonSlotMenu.GetItemSubmenuNode(data.Index);
		data.IndexToToolItem.Clear();
		menu.Clear();

		// Create tool items
		int i = 0;
		foreach (AddonToolItem item in obj.ToolItems)
		{
			int myI = i++;
			menu.AddItem(item.Text);
			data.IndexToToolItem[menu.GetItemIndex(myI)] = item;
		}
	}

	public void RemoveAddonMenu(AddonObject obj)
	{
		if (_addonDataByRoot.TryGetValue(obj.Root, out var rootAddons) &&
			rootAddons.TryGetValue(obj.Identifier, out AddonMenuData? data))
		{
			// Remove from UI if it's currently displayed
			if (obj.Root == _currentRoot && data.Visible)
			{
				_addonSlotMenu.RemoveItem(_addonSlotMenu.GetItemIndex(data.ItemId));
			}

			rootAddons.Remove(obj.Identifier);

			// Clean up empty root dictionaries
			if (rootAddons.Count == 0)
			{
				_addonDataByRoot.Remove(obj.Root);
			}
		}
	}

	public void SwitchTo(World? game)
	{
		bool disabled = game == null;
		foreach ((MenuButtonMenus mbtn, MenuItem[] items) in _menus)
		{
			if (mbtn.DevOnly) continue;
			if (mbtn.RequireGameOpen)
			{
				mbtn.Button.Disabled = disabled;
			}
			foreach (MenuItem item in items)
			{
				if (item is MenuButtonItem btnI && btnI.RequireGameOpen)
				{
					mbtn.Popup.SetItemDisabled(btnI.Index, disabled);
				}
			}
		}

		// Switch addon menus to the new root
		SwitchAddonRoot(game);
	}

	private void SwitchAddonRoot(World? newRoot)
	{
		// Remove all current addon menus from UI
		if (_currentRoot != null && _addonDataByRoot.TryGetValue(_currentRoot, out var currentAddons))
		{
			foreach (var data in currentAddons.Values)
			{
				if (data.Visible)
				{
					_addonSlotMenu.RemoveItem(_addonSlotMenu.GetItemIndex(data.ItemId));
					data.Visible = false;
				}
			}
		}

		// Add addon menus for the new root to UI
		_currentRoot = newRoot;
		if (newRoot != null && _addonDataByRoot.TryGetValue(newRoot, out var newAddons))
		{
			foreach (var kvp in newAddons)
			{
				UpdateAddonMenuInUI(kvp.Value.AddonObject, kvp.Value);
			}
		}
	}

	private void OnPoly(long idx)
	{
		switch (idx)
		{
			case 0: // About Polytoria
				{
					CreatorService.Interface.PopupCredits();
					break;
				}
			case 1: // Startup splash
				{
					StartupSplash.Singleton.Show();
					break;
				}
		}
	}
}
