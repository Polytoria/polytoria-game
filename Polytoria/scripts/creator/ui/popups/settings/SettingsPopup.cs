// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Settings;
using Polytoria.Creator.UI.Components;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class SettingsPopup : PopupWindowBase
{
	private const string SettingsPropertyPath = "res://scenes/creator/popups/settings/components/settings_property.tscn";
	[Export] private Tree _categoryTree = null!;
	[Export] private Control _layout = null!;

	private static readonly IReadOnlyDictionary<string, List<SettingDef>> SectionDefs =
		CreatorSettingsRegistry.Definitions.Values
			.GroupBy(d => d.SectionKey)
			.ToDictionary(g => g.Key, g => g.ToList());

	private static readonly IReadOnlyList<SettingSectionDef> SortedSections =
		CreatorSettingsRegistry.Sections.OrderBy(s => s.SortOrder).ToArray();

	private readonly Dictionary<TreeItem, string> _itemToSectionKey = [];
	private readonly Dictionary<string, List<SettingsPropertyUI>> _sectionUIs = [];
	private string _activeSection = string.Empty;

	public override void _Ready()
	{
		TreeItem root = _categoryTree.CreateItem();
		TreeItem? firstSelected = null;

		foreach (var section in SortedSections)
		{
			if (!SectionDefs.TryGetValue(section.Key, out var defs) || defs.Count == 0) continue;

			TreeItem ch = root.CreateChild();
			ch.SetText(0, section.Label);
			_itemToSectionKey[ch] = section.Key;

			firstSelected ??= ch;
		}

		_categoryTree.ItemSelected += OnItemSelected;
		firstSelected?.Select(0);
		base._Ready();
	}

	public override void _ExitTree()
	{
		_categoryTree.ItemSelected -= OnItemSelected;
		base._ExitTree();
	}

	private void OnItemSelected()
	{
		if (!_itemToSectionKey.TryGetValue(_categoryTree.GetSelected(), out string sectionKey))
			return;

		if (sectionKey == _activeSection)
			return;

		if (_sectionUIs.TryGetValue(_activeSection, out var prevUIs))
		{
			foreach (var ui in prevUIs)
				ui.Visible = false;
		}

		_activeSection = sectionKey;

		if (!_sectionUIs.TryGetValue(sectionKey, out var cachedUIs))
		{
			cachedUIs = [];
			if (!SectionDefs.TryGetValue(sectionKey, out var defs))
				return;
			foreach (SettingDef def in defs)
			{
				SettingsPropertyUI ui = Globals.CreateInstanceFromScene<SettingsPropertyUI>(SettingsPropertyPath);
				ui.Init(def, CreatorSettingsService.Instance);
				cachedUIs.Add(ui);
				_layout.AddChild(ui);
			}

			_sectionUIs[sectionKey] = cachedUIs;
		}
		else
		{
			foreach (var ui in cachedUIs)
				ui.Visible = true;
		}
	}
}
