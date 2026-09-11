// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Services;

[Static("Assets")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class AssetsService : Instance
{
	public readonly Dictionary<string, FileLinkAsset> FileLinks = [];

	public override void PreDelete()
	{
		FileLinks.Clear();
		base.PreDelete();
	}

	[ScriptMethod, Obsolete("Use the class's corresponding .New static instead")]
	public BaseAsset? NewAsset(string assetClassName)
	{
		NetworkedObject? obj = NewInternal(assetClassName, Root);
		if (obj is not BaseAsset)
		{
			obj?.Destroy();
			return null;
		}
		return (BaseAsset)obj;
	}

	[ScriptMethod, Obsolete(use: "PTImageAsset.New")]
	public PTImageAsset? NewPTImage(uint imgID)
	{
		PTImageAsset ptImg = New<PTImageAsset>();
		ptImg.ImageID = imgID;
		return ptImg;
	}

	[ScriptMethod, Obsolete(use: "PTAudioAsset.New")]
	public PTAudioAsset? NewPTAudio(uint audioID)
	{
		PTAudioAsset ptAudio = New<PTAudioAsset>();
		ptAudio.AudioID = audioID;
		return ptAudio;
	}

	[ScriptMethod, Obsolete(use: "PTMeshAsset.New")]
	public PTMeshAsset? NewPTMesh(uint assetID)
	{
		PTMeshAsset ptMesh = New<PTMeshAsset>();
		ptMesh.AssetID = assetID;
		return ptMesh;
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public FileLinkAsset GetFileLinkByPath(string path)
	{
		if (FileLinks.TryGetValue(Root.IO.GetIDFromPath(path), out FileLinkAsset? link)) return link;
		FileLinkAsset fl = new()
		{
			Root = Root,
			LinkedID = Root.IO.GetIDFromPath(path),
		};
		return fl;
	}


	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public FileLinkAsset GetFileLinkByID(string id)
	{
		if (FileLinks.TryGetValue(id, out FileLinkAsset? link)) return link;
		FileLinkAsset fl = new()
		{
			Root = Root,
			LinkedID = id,
		};
		return fl;
	}

	internal void RegisterFileLink(FileLinkAsset fl)
	{
		FileLinks.TryAdd(fl.LinkedID, fl);
	}

	internal void DeregisterFileLink(FileLinkAsset fl)
	{
		FileLinks.Remove(fl.LinkedID);
	}
}
