// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using static Polytoria.Scripting.LogDispatcher;

namespace Polytoria.Creator.UI;

public partial class ConsoleFilters : PopupPanel
{
	[Export] private CheckBox _infoCheckBox = null!;
	[Export] private CheckBox _warningCheckBox = null!;
	[Export] private CheckBox _errorCheckBox = null!;

	[Export] private CheckBox _noneCheckBox = null!;
	[Export] private CheckBox _clientCheckBox = null!;
	[Export] private CheckBox _serverCheckBox = null!;
	[Export] private CheckBox _addonCheckBox = null!;

	private DebugConsole? _debugConsole;

	public override void _Ready()
	{
		_debugConsole = DebugConsole.Singleton;

		// Connect Type-Filter CheckBoxes
		_infoCheckBox.Toggled += enabled => _debugConsole.ToggleTypeFilter(LogTypeEnum.Info, enabled);
		_warningCheckBox.Toggled += enabled => _debugConsole.ToggleTypeFilter(LogTypeEnum.Warning, enabled);
		_errorCheckBox.Toggled += enabled => _debugConsole.ToggleTypeFilter(LogTypeEnum.Error, enabled);

		// Connect Source-Filter CheckBoxes
		_noneCheckBox.Toggled += enabled => _debugConsole.ToggleSourceFilter(LogFromEnum.None, enabled);
		_clientCheckBox.Toggled += enabled => _debugConsole.ToggleSourceFilter(LogFromEnum.Client, enabled);
		_serverCheckBox.Toggled += enabled => _debugConsole.ToggleSourceFilter(LogFromEnum.Server, enabled);
		_addonCheckBox.Toggled += enabled => _debugConsole.ToggleSourceFilter(LogFromEnum.Addon, enabled);

	}

	private void SyncCheckboxes()
	{
		if (_debugConsole == null) return;

		_infoCheckBox.ButtonPressed = _debugConsole.IsTypeFilterEnabled(LogTypeEnum.Info);
		_warningCheckBox.ButtonPressed = _debugConsole.IsTypeFilterEnabled(LogTypeEnum.Warning);
		_errorCheckBox.ButtonPressed = _debugConsole.IsTypeFilterEnabled(LogTypeEnum.Error);

		_noneCheckBox.ButtonPressed = _debugConsole.IsSourceFilterEnabled(LogFromEnum.None);
		_clientCheckBox.ButtonPressed = _debugConsole.IsSourceFilterEnabled(LogFromEnum.Client);
		_serverCheckBox.ButtonPressed = _debugConsole.IsSourceFilterEnabled(LogFromEnum.Server);
		_addonCheckBox.ButtonPressed = _debugConsole.IsSourceFilterEnabled(LogFromEnum.Addon);
	}

	private void ShowAtPosition(Vector2 screenPos)
	{
		if (Visible)
		{
			Hide();
			return;
		}

		SyncCheckboxes();
		ResetSize();

		Popup(new Rect2I(
			(Vector2I)screenPos,
			(Vector2I)Size
		));
	}

}
