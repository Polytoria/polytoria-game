// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTAudioAsset : AudioAsset
{
	private uint _audioID = 0;

	[Editable, ScriptProperty]
	public uint AudioID
	{
		get => _audioID;
		set
		{
			_audioID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void LoadResource()
	{
		if (AudioID == 0) return;
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Audio, ID = AudioID },
			InvokeResourceLoaded
		);
	}
}
