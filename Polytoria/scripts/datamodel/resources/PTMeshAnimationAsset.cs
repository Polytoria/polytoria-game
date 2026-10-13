// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTMeshAnimationAsset : MeshAnimationAsset
{
	private uint _assetID;

	[Editable, ScriptProperty]
	public uint AssetID
	{
		get => _assetID;
		set
		{
			_assetID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public override void LoadResource()
	{
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Mesh, ID = AssetID },
			OnMeshResourceLoaded
		);
	}

	private void OnMeshResourceLoaded(Resource res)
	{
		if (res is PackedScene scene)
		{
			Node obj = scene.Instantiate<Node>();
			AnimationPlayer? animPlay = obj.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

			if (animPlay != null)
			{
				var libList = animPlay.GetAnimationLibraryList();
				if (libList.Count != 0)
				{
					AnimationLibrary lib = animPlay.GetAnimationLibrary(libList[0]);
					AnimationLibrary flib = (AnimationLibrary)lib.DuplicateDeep();
					InvokeResourceLoaded(flib);
				}
			}

			obj.Free();
			obj.Dispose();
		}
	}
}
