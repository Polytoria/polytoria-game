// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIButton : UILabel
{
	private Color _pressedColor = new(0.6f, 0.6f, 0.6f, 1);
	private Color _hoverColor = new(0.8f, 0.8f, 0.8f, 1);
	private Color _normalColor = new(1f, 1f, 1f, 1);
	private bool _pressedOnButton;
	[ScriptProperty] public PTSignal Clicked { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		NodeControl.MouseDefaultCursorShape = Godot.Control.CursorShape.PointingHand;
		NodeControl.FocusMode = Control.FocusModeEnum.None;

		MouseDown.Connect(() =>
		{
			_pressedOnButton = true;
			NodeControl.Modulate = _pressedColor;
		});

		MouseUp.Connect(() =>
		{
			NodeControl.Modulate = _hoverColor;
			if (_pressedOnButton)
			{
				_pressedOnButton = false;
				Clicked.Invoke();
			}
		});

		MouseEnter.Connect(() =>
		{
			NodeControl.Modulate = _hoverColor;
		});

		MouseExit.Connect(() =>
		{
			_pressedOnButton = false;
			NodeControl.Modulate = _normalColor;
		});
	}
}
