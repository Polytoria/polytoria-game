// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using static Polytoria.Datamodel.CharacterModel;

namespace Polytoria.Shared.Misc;

/// <summary>
/// Class for bridging CharacterModel's state with Godot's
/// </summary>
public partial class CharacterAnimHelper : Node
{
	public CharacterModel Target = null!;

	public bool StateIdle => Target.CurrentState == CharacterModelStateEnum.Idle;
	public bool StateWalking => Target.CurrentState == CharacterModelStateEnum.Walking;
	public bool StateRunning => Target.CurrentState == CharacterModelStateEnum.Running;
	public bool StateJumping => Target.CurrentState == CharacterModelStateEnum.Jumping;
	public bool StateClimbing => Target.CurrentState == CharacterModelStateEnum.Climbing;

	private bool _idle2;
	private bool _idle3;

	public bool Idle2
	{
		get => _idle2;
		set
		{
			_idle2 = value;
			if (value) _idle3 = false;
		}
	}

	public bool Idle3
	{
		get => _idle3;
		set
		{
			_idle3 = value;
			if (value) _idle2 = false;
		}
	}

	public bool PanicFall = false;
	public bool JustJumped = false;
}
