// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class BuiltInAudioAsset : AudioAsset
{
	private BuiltInAudioPresetEnum _audioPreset;

	[Editable, ScriptProperty]
	public BuiltInAudioPresetEnum AudioPreset
	{
		get => _audioPreset;
		set
		{
			_audioPreset = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	private readonly Dictionary<BuiltInAudioPresetEnum, string> AudioMapping = new()
	{
		{ BuiltInAudioPresetEnum.Jump, "jump.ogg" },
		{ BuiltInAudioPresetEnum.Explosion, "explosion.ogg" },
	};

	public static void RegisterAsset()
	{
		RegisterType<BuiltInAudioAsset>();
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(GD.Load<AudioStream>(Globals.BuiltInAudioLocation.PathJoin(AudioMapping[_audioPreset])));
	}

	[ScriptEnum]
	public enum BuiltInAudioPresetEnum { Jump, Explosion };
}
