// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;

namespace Polytoria.Mobile.UI;

public partial class NewUserSplash : Control
{
	[Export] private Button _registerBtn = null!;
	[Export] private Button _loginBtn = null!;
	[Export] private Button _closeBtn = null!;


	public override void _Ready()
	{
		_closeBtn.Visible = OS.IsDebugBuild();

		_registerBtn.Pressed += OnRegisterPressed;
		_loginBtn.Pressed += OnLoginPressed;
		_closeBtn.Pressed += OnClosePressed;
	}

	private void OnClosePressed()
	{
		Visible = false;
		MobileUI.Singleton.SwitchTo(MobileViewEnum.Home);
	}

	public void ShowSplash()
	{
		GetNode<AnimationPlayer>("AnimPlay").Play("appear");
	}

	private void OnRegisterPressed()
	{
		OpenAuthMobile();
	}

	private void OnLoginPressed()
	{
		OpenAuthMobile();
	}

	private static void OpenAuthMobile()
	{
		PolyMobileAuthAPI.StartMobileAuth();
	}
}
