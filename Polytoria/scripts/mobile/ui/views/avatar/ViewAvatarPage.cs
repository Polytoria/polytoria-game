// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Mobile.UI;

public partial class ViewAvatarPage : MobileViewBase
{
	//private PolytorianModel _polytorian = null!;


	public override void _Ready()
	{
		//_polytorian.LoadAppearance(PolyMobileAuthAPI.CurrentUserInfo.Id);
	}

	public override void ShowView(object? args)
	{
		base.ShowView(args);
		StrikeAPose();
	}

	private static void StrikeAPose()
	{
		//_polytorian.Animator.PlayOneShotAnimation("avataredit_pose" + Mathf.Round(GD.RandRange(1, 3)));
	}
}
