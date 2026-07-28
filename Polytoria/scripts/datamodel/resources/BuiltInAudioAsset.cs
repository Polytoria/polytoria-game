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
		{ BuiltInAudioPresetEnum.Explosion, "explosion.ogg" },

		{ BuiltInAudioPresetEnum.Jump, "jump.ogg" },
		{ BuiltInAudioPresetEnum.Fall, "fall.ogg" },
		{ BuiltInAudioPresetEnum.Land, "land.ogg" },

		{ BuiltInAudioPresetEnum.FootstepPlastic, "footsteps/plastic.ogg" },
		{ BuiltInAudioPresetEnum.FootstepGrass, "footsteps/grass.ogg" },
		{ BuiltInAudioPresetEnum.FootstepWood, "footsteps/wood.ogg" },
		{ BuiltInAudioPresetEnum.FootstepPlanks, "footsteps/planks.ogg" },
		{ BuiltInAudioPresetEnum.FootstepMetal, "footsteps/metal.ogg" },
		{ BuiltInAudioPresetEnum.FootstepPlate, "footsteps/plate.ogg" },
		{ BuiltInAudioPresetEnum.FootstepStone, "footsteps/stone.ogg" },
		{ BuiltInAudioPresetEnum.FootstepDirt, "footsteps/dirt.ogg" },
		{ BuiltInAudioPresetEnum.FootstepFabric, "footsteps/fabric.ogg" },
		{ BuiltInAudioPresetEnum.FootstepIce, "footsteps/ice.ogg" },
		{ BuiltInAudioPresetEnum.FootstepSand, "footsteps/sand.ogg" }

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
	public enum BuiltInAudioPresetEnum { Explosion, Jump, Fall, Land, FootstepPlastic, FootstepGrass, FootstepWood, FootstepPlanks, FootstepMetal, FootstepPlate, FootstepStone, FootstepDirt, FootstepFabric, FootstepIce, FootstepSand };
}
