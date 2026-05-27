// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Shared.Misc;

/// <summary>
/// Class for bridging inputs with Godot
/// </summary>
public partial class InputHelper : Node
{
	public event Action<InputEvent>? GodotInputEvent;
	public event Action<InputEvent>? GodotUnhandledInputEvent;

	public override void _Input(InputEvent @event)
	{
		GodotInputEvent?.Invoke(@event);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		GodotUnhandledInputEvent?.Invoke(@event);
		base._Input(@event);
	}
}
