// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings;
using System.Linq;

namespace Polytoria.Client.UI;

public sealed partial class UIMenuSettings : UIMenuViewBase
{
	[Export] private Control _viewContainer = null!;
	[Export] private VBoxContainer _categoryContainer = null!;

	private readonly ButtonGroup _categoryButtonGroup = new();
	private readonly System.Collections.Generic.Dictionary<string, Button> _categoryButtons = [];
	private string? _currentSectionKey;


	public override void _Ready()
	{
		BuildCategories();
		base._Ready();
	}

	public override void ShowView()
	{
		string firstSection = ClientSettingsRegistry.Sections.OrderBy(s => s.SortOrder).First(s => IsSectionVisible(s.Key)).Key;
		SwitchSection(firstSection);
		base.ShowView();
	}

	private void BuildCategories()
	{
		_categoryButtons.Clear();

		foreach (Node child in _categoryContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var section in ClientSettingsRegistry.Sections.OrderBy(s => s.SortOrder))
		{
			if (!IsSectionVisible(section.Key)) continue;

			string sectionKey = section.Key;
			Button btn = CreateCategoryButton(section);
			btn.ButtonGroup = _categoryButtonGroup;

			btn.Pressed += () => SwitchSection(sectionKey);
			_categoryContainer.AddChild(btn);
			_categoryButtons[sectionKey] = btn;
		}
	}

	private static bool IsSectionVisible(string sectionKey)
	{
		var defs = ClientSettingsRegistry.Definitions.Values.Where(d => d.SectionKey == sectionKey).ToArray();
		return defs.Length == 0 || defs.Any(d => d.IsSupported);
	}

	private static Button CreateCategoryButton(Shared.Settings.SettingSectionDef section)
	{
		Button btn = new()
		{
			ToggleMode = true,
			CustomMinimumSize = new Vector2(0, 50),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			Text = string.Empty,
			FocusMode = FocusModeEnum.All
		};

		HBoxContainer layout = new()
		{
			MouseFilter = MouseFilterEnum.Ignore
		};
		layout.AddThemeConstantOverride("separation", 12);
		layout.SetAnchorsPreset(LayoutPreset.FullRect);
		layout.OffsetLeft = 16;
		layout.OffsetRight = -16;
		btn.AddChild(layout);

		if (!string.IsNullOrEmpty(section.IconPath))
		{
			TextureRect icon = new()
			{
				MouseFilter = MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(28, 0),
				Texture = GD.Load<Texture2D>(section.IconPath),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
			};
			layout.AddChild(icon);
		}

		Label label = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Text = section.Label,
			VerticalAlignment = VerticalAlignment.Center
		};
		label.AddThemeFontSizeOverride("font_size", 16);
		layout.AddChild(label);

		return btn;
	}

	private void SwitchSection(string key)
	{
		if (_currentSectionKey == key)
		{
			return;
		}

		_currentSectionKey = key;
		UpdateCategoryButtons();

		foreach (Node child in _viewContainer.GetChildren())
		{
			child.QueueFree();
		}

		SettingsSectionPage page = new()
		{
			SectionKey = key
		};

		_viewContainer.AddChild(page);
	}

	private void UpdateCategoryButtons()
	{
		foreach (var pair in _categoryButtons)
		{
			pair.Value.SetPressedNoSignal(pair.Key == _currentSectionKey);
		}
	}
}
