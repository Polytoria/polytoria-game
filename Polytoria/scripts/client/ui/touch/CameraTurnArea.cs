// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Touch;

public partial class CameraTurnArea : InputFallbackBase
{
	public const float MobileZoomMultipler = 4;
	private bool _justZoomed = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventScreenDrag eventDrag)
		{
			if (_justZoomed)
			{
				_justZoomed = false;
				return;
			}
			World.Current!.Environment.CurrentCamera?.ReceiveDragTouchInput(eventDrag);
		}
		else if (@event is InputEventMagnifyGesture mag)
		{
			_justZoomed = true;
			Camera? camera = World.Current!.Environment.CurrentCamera;
			if (camera == null) return;
			float length = 1 - mag.Factor;
			float zoomDelta = length * camera.ScrollSensitivity * MobileZoomMultipler;
			camera.Distance += zoomDelta;
		}
	}
}
