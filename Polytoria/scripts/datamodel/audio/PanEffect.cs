// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PanEffect : AudioEffectBase
{
	private float _pan = 0f;

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float Pan
	{
		get => _pan;
		set
		{
			_pan = Mathf.Clamp(value, -1f, 1f);
			var fx = GetLive<AudioEffectPanner>();
			if (fx != null) fx.Pan = _pan;
			OnPropertyChanged();
		}
	}

	protected override AudioEffect CreateEffect()
	{
		return new AudioEffectPanner { Pan = _pan };
	}
}
