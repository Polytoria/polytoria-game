// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;

namespace Polytoria.Creator.UI.Menus;

public partial class PlayOptionsMenu : Control
{
	[Export] private Button _playBtn = null!;
	[Export] private Button _playAtCamBtn = null!;
	[Export] private Button _stopBtn = null!;
	[Export] private MenuButton _playerCountMenu = null!;
	[Export] private CheckBox _clearOnPlay = null!;
	private PopupMenu _plrCountPopup = null!;

	public override void _Ready()
	{
		_playBtn.Pressed += OnPlayButtonPressed;
		_playAtCamBtn.Pressed += OnPlayAtCamPressed;
		_stopBtn.Pressed += OnStopButtonPressed;
		_stopBtn.Disabled = true;
		_plrCountPopup = _playerCountMenu.GetPopup();
		_plrCountPopup.IdPressed += OnPlrCountIdPressed;

		_playBtn.GuiInput += OnPlayBtnGUIInput;

		OnPlrCountIdPressed(1);

		CreatorService.Singleton.LocalTestStarted.Connect(OnLocalTestStarted);
		CreatorService.Singleton.LocalTestStopped.Connect(OnLocalTestStopped);
	}

	private void OnPlayBtnGUIInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Right && btn.Pressed)
		{
			_playerCountMenu.ShowPopup();
		}
	}

	private void OnPlrCountIdPressed(long id)
	{
		for (int i = 0; i < _plrCountPopup.ItemCount; i++)
		{
			_plrCountPopup.SetItemChecked(i, false);
		}
		_plrCountPopup.SetItemChecked(_plrCountPopup.GetItemIndex((int)id), true);
		CreatorService.Singleton.LocalTestPlayerCount = (int)id;
	}

	private void OnLocalTestStarted()
	{
		_playBtn.Disabled = true;
		_playAtCamBtn.Disabled = true;
		_stopBtn.Disabled = false;
	}

	private void OnLocalTestStopped()
	{
		_playBtn.Disabled = false;
		_playAtCamBtn.Disabled = false;
		_stopBtn.Disabled = true;
	}

	private void OnPlayButtonPressed()
	{
		CreatorService.Singleton.StartLocalTest(false, _clearOnPlay.ButtonPressed);
	}

	private void OnPlayAtCamPressed()
	{
		CreatorService.Singleton.StartLocalTest(true, _clearOnPlay.ButtonPressed);
	}

	private void OnStopButtonPressed()
	{
		CreatorService.Singleton.StopLocalTest();
	}
}
