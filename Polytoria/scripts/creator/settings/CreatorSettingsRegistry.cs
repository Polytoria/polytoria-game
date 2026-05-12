// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Shared.Settings;
using System.Collections.Generic;

namespace Polytoria.Creator.Settings;

public static class CreatorSettingsRegistry
{
	private const string DefaultSectionIcon = "res://assets/textures/ui-icons/settings.svg";

	public static readonly IReadOnlyList<SettingSectionDef> Sections =
	[
		new() { Key = "creator", Label = "Creator", IconPath = DefaultSectionIcon, SortOrder = 0 },
		new() { Key = "interface", Label = "Interface", IconPath = DefaultSectionIcon, SortOrder = 1 },
		new() { Key = "display", Label = "Display", IconPath = "res://assets/textures/ui-icons/camera.svg", SortOrder = 2 },
		new() { Key = "graphics", Label = "Graphics", IconPath = "res://assets/textures/ui-icons/mountain.svg", SortOrder = 3 },
		new() { Key = "post_processing", Label = "Post Processing", IconPath = "res://assets/textures/ui-icons/rocket.svg", SortOrder = 4 },
		new() { Key = "backup", Label = "Backup", IconPath = DefaultSectionIcon, SortOrder = 5 },
		new() { Key = "code_editor", Label = "Code Editor", IconPath = DefaultSectionIcon, SortOrder = 6 },
		new() { Key = "popups", Label = "Popups", IconPath = DefaultSectionIcon, SortOrder = 7 },
	];

	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>();
		SharedSettingsRegistry.AddSharedTo(defs);

		// Creator
		defs.Add(CreatorSettingKeys.Creator.OpenWebAfterPublish,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.OpenWebAfterPublish,
				SectionKey = "creator",
				Label = "Open Web after Publish",
				Description = "Open the published item in a browser after publishing.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		// Interface
		defs.Add(CreatorSettingKeys.Interface.UiScale,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Interface.UiScale,
				SectionKey = "interface",
				Label = "UI Scale",
				Description = "Scale of the creator interface.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 1.0f,
				MinValue = 0.5f,
				MaxValue = 5f,
				Step = 0.25f
			});

		// Backup
		defs.Add(CreatorSettingKeys.Backup.MaxBackupCount,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.Backup.MaxBackupCount,
				SectionKey = "backup",
				Label = "Max Backup Count",
				Description = "Maximum number of backups to keep.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 10,
				MinValue = 1,
				MaxValue = 50,
				Step = 1
			});

		defs.Add(CreatorSettingKeys.Backup.BackupInterval,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Backup.BackupInterval,
				SectionKey = "backup",
				Label = "Backup Interval (minutes)",
				Description = "How often to automatically back up worlds.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 4f,
				MinValue = 1f,
				MaxValue = 60f,
				Step = 1f
			});

		// Code Editor
		defs.Add(CreatorSettingKeys.CodeEditor.PreferredEditor,
			new SettingDef<PreferredEditorEnum>
			{
				Key = CreatorSettingKeys.CodeEditor.PreferredEditor,
				SectionKey = "code_editor",
				Label = "Preferred Editor",
				Description = "Editor to use when opening scripts.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = PreferredEditorEnum.BuiltIn,
				Options =
				[
					new() { Value = PreferredEditorEnum.BuiltIn, Label = "Built-in" },
					new() { Value = PreferredEditorEnum.SystemDefault, Label = "System Default" },
					new() { Value = PreferredEditorEnum.VSCode, Label = "VS Code" },
					new() { Value = PreferredEditorEnum.Zed, Label = "Zed" },
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.IndentationMode,
			new SettingDef<IndentationModeEnum>
			{
				Key = CreatorSettingKeys.CodeEditor.IndentationMode,
				SectionKey = "code_editor",
				Label = "Indentation Mode",
				Description = "Use tabs or spaces for indentation.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = IndentationModeEnum.Tabs,
				Options =
				[
					new() { Value = IndentationModeEnum.Tabs, Label = "Tabs" },
					new() { Value = IndentationModeEnum.Spaces, Label = "Spaces" },
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.IndentationSize,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.CodeEditor.IndentationSize,
				SectionKey = "code_editor",
				Label = "Indentation Size (In Spaces)",
				Description = "Number of spaces per indentation level.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 2,
				MinValue = 1,
				MaxValue = 8,
				Step = 1
			});

		// Popups
		defs.Add(CreatorSettingKeys.Popups.CloseModelWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.CloseModelWarning,
				SectionKey = "popups",
				Label = "Close Model Warning",
				Description = "Show warning when closing an unsaved model.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.MoveFileConfirmation,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.MoveFileConfirmation,
				SectionKey = "popups",
				Label = "Move File Confirmation",
				Description = "Show confirmation when moving files.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.CloseTabWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.CloseTabWarning,
				SectionKey = "popups",
				Label = "Close Tab Warning",
				Description = "Show warning when closing a modified tab.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
