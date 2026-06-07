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
	[Export] private OptionButton _playerCountOption = null!;

	public override void _Ready()
	{
		_playBtn.Pressed += OnPlayButtonPressed;
		_playAtCamBtn.Pressed += OnPlayAtCamPressed;
		_stopBtn.Pressed += OnStopButtonPressed;
		_stopBtn.Disabled = true;

		_playerCountOption.ItemSelected += OnPlayerCountSelected;
		_playerCountOption.Select(0);
		_playerCountOption.Text = _playerCountOption.GetItemId(0).ToString();

		CreatorService.Singleton.LocalTestStarted.Connect(OnLocalTestStarted);
		CreatorService.Singleton.LocalTestStopped.Connect(OnLocalTestStopped);
	}

	private void OnPlayerCountSelected(long index)
	{
		CreatorService.Singleton.LocalTestPlayerCount = (int)_playerCountOption.GetItemId((int)index);
		_playerCountOption.Text = _playerCountOption.GetItemId((int)index).ToString();
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
		CreatorService.Singleton.StartLocalTest();
	}

	private void OnPlayAtCamPressed()
	{
		CreatorService.Singleton.StartLocalTest(true);
	}

	private void OnStopButtonPressed()
	{
		CreatorService.Singleton.StopLocalTest();
	}
}
