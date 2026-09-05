// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Truss : Part
{
	private float _climbSpeed;
	private bool _climbable;

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float ClimbSpeed
	{
		get => _climbSpeed;
		set
		{
			_climbSpeed = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Climbable
	{
		get => _climbable;
		set
		{
			_climbable = value;
			OnPropertyChanged();
		}
	}
}
