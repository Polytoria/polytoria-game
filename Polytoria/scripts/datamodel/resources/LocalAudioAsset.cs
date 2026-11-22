// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class LocalAudioAsset : AudioAsset
{
	private string _audioPath = null!;
	private AudioStream _audio = null!;

	[Editable, ScriptProperty]
	public string AudioPath
	{
		get => _audioPath;
		set
		{
			_audioPath = value;

			switch (_audioPath.GetExtension().ToLower())
			{
				case "mp3":
					_audio = AudioStreamMP3.LoadFromFile(_audioPath);
					break;
				case "wav":
					_audio = AudioStreamWav.LoadFromFile(_audioPath);
					break;
				case "ogg":
					_audio = AudioStreamOggVorbis.LoadFromFile(_audioPath);
					break;
			}

			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(_audio);
	}

}
