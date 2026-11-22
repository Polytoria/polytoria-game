// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;

namespace Polytoria.Client.UI.Core;

public partial class DisconnectedUI : CanvasLayer
{
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private Label _titleLabel = null!;
	[Export] private Label _reasonLabel = null!;
	[Export] private Label _codeLabel = null!;
	[Export] private Button _quitButton = null!;

	private ClientEntry _entry = null!;

	public override void _Ready()
	{
		if (GetNodeOrNull("../") is not ClientEntry)
		{
			QueueFree();
			return;
		}
		_entry = GetNode<ClientEntry>("../");

		Visible = false;
		_entry.NetworkEssentialsReady += OnNetworkEssentialsReady;
		_quitButton.Pressed += _entry.LeaveGame;
	}

	private void OnNetworkEssentialsReady()
	{
		_entry.NetworkService.ClientDisconnected += OnClientDisconnected;
	}

	private void OnClientDisconnected(string reason, NetworkService.DisconnectionCodeEnum code)
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_reasonLabel.Text = reason;
		_codeLabel.Text = Tr("Code: ") + code;
		_animPlay.Play("appear");
	}
}
