// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Creator.Settings;

public static class CreatorSettingKeys
{
	public static class Creator
	{
		public const string OpenWebAfterPublish = "creator.open_web_after_publish";
	}

	public static class Interface
	{
		public const string UiScale = "interface.ui_scale";
	}

	public static class Backup
	{
		public const string MaxBackupCount = "backup.max_backup_count";
		public const string BackupInterval = "backup.backup_interval";
	}

	public static class CodeEditor
	{
		public const string PreferredEditor = "code_editor.preferred_editor";
		public const string IndentationMode = "code_editor.indentation_mode";
		public const string IndentationSize = "code_editor.indentation_size";
	}

	public static class Popups
	{
		public const string CloseModelWarning = "popups.close_model_warning";
		public const string MoveFileConfirmation = "popups.move_file_confirmation";
		public const string CloseTabWarning = "popups.close_tab_warning";
	}
}
