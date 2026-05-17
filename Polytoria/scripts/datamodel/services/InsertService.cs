// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
#if CREATOR
using Polytoria.Datamodel.Creator;
#endif

namespace Polytoria.Datamodel.Services;

[Static("Insert"), ExplorerExclude, SaveIgnore]
public sealed partial class InsertService : Instance
{
	private readonly PTHttpClient _httpClient = new();
	private static readonly Dictionary<int, APIStoreItem> _storeItemCache = new();

	[ScriptMethod, Attributes.Obsolete("Use ModelAsync instead")]
	public void Model(int id, PTCallback? callback = null)
	{
		_ = ModelAsync(id).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				callback?.Invoke(tsk.Result);
			}
		});
	}

	[ScriptMethod]
	public NPC DefaultNPC()
	{
		var npc = New<NPC>();
		InitializeDefaultNPC(npc);
		return npc;
	}

	[ScriptMethod]
	public void InitializeDefaultNPC(NPC npc)
	{
		int owner = npc.NetworkAuthority;

		// Default character
		var ptm = DefaultCharacter();
		npc.Character = ptm;
		ptm.Name = "Character";
		ptm.Parent = npc;
		ptm.LocalPosition = Vector3.Zero;
		ptm.LocalRotation = Vector3.Zero;
		ptm.LocalSize = Vector3.One;
		ptm.SetNetworkAuthority(npc.NetworkAuthority, false);
		ptm.Animator?.SetNetworkAuthority(owner, false);

		// Jump sound
		BuiltInAudioAsset audio = New<BuiltInAudioAsset>();
		audio.AudioPreset = BuiltInAudioAsset.BuiltInAudioPresetEnum.Jump;
		var jumpSound = New<Sound>();
		jumpSound.Name = "JumpSound";
		jumpSound.Parent = npc;
		jumpSound.Volume = 0.5f;
		jumpSound.Audio = audio;
		jumpSound.Autoplay = false;
		jumpSound.Loop = false;
		jumpSound.PlayInWorld = true;
		jumpSound.SetNetworkAuthority(owner, false);

		npc.JumpSound = jumpSound;

		jumpSound.LocalPosition = Vector3.Zero;
		jumpSound.LocalRotation = Vector3.Zero;
		jumpSound.LocalSize = Vector3.One;
	}

	[ScriptMethod]
	public PolytorianModel DefaultCharacter()
	{
		var ptm = New<PolytorianModel>();
		var animator = New<Animator>();
		animator.AutoInit = false;
		animator.Name = "Animator";
		animator.Parent = ptm;
		ptm.Animator = animator;

		return ptm;
	}

	[ScriptMethod]
	public async Task<Instance?> ModelAsync(int id)
	{
		using HttpResponseMessage msg = await _httpClient.GetAsync(Globals.ApiEndpoint.PathJoin("/v1/models/get-model?id=" + id));
		byte[] modelBytes = await msg.Content.ReadAsByteArrayAsync();
		Instance? model = await DatamodelLoader.LoadModelBytes(Root, modelBytes, Root.TemporaryContainer);
		return model;
	}

#if CREATOR
	public async Task<Instance?> CreatorImportWebModel(int id, string? optionalName = null)
	{
		using HttpResponseMessage msg = await _httpClient.GetAsync(Globals.ApiEndpoint.PathJoin("/v1/models/get-model?id=" + id));
		byte[] modelBytes = await msg.Content.ReadAsByteArrayAsync();

		if (optionalName != null)
		{
			string importFolderName = await DatamodelLoader.GetImportFolderName(modelBytes);

			if (Root.LinkedSession.FileExists(Globals.ToolboxFolderName + "/" + importFolderName + "/"))
			{
				if (!await CreatorService.Interface.PromptConfirmation(importFolderName + " already exists, do you want to update it?")) return null;
			}
		}

		Instance? model = await DatamodelLoader.LoadModelBytes(Root, modelBytes, Root.TemporaryContainer, optionalName);
		return model;
	}
#endif

	[ScriptMethod]
	public async Task<Accessory?> AccessoryAsync(int id)
	{
		APIStoreItem storeItem = await GetStoreItemCachedAsync(id);

		PTMeshAsset meshAsset = New<PTMeshAsset>();
		meshAsset.AssetID = (uint)id;

		Accessory accessory = New<Accessory>(this);
		Mesh mesh = New<Mesh>();
		mesh.Size = Vector3.One;
		mesh.Parent = accessory;
		mesh.Asset = meshAsset;

		accessory.LocalRotation = Vector3.Zero;
		mesh.LocalRotation = Vector3.Zero;
		accessory.Size = new Vector3(0.5f, 0.5f, 0.5f);

		mesh.IncludeOffset = true;
		mesh.Name = "Mesh";
		mesh.CanCollide = false;
		mesh.Anchored = true;
		accessory.Name = storeItem.Name;

		mesh.LocalPosition = new Vector3(0, -10.7f, 0);

		string? accessoryType = storeItem.AccessoryType;

		if (accessoryType == "backAccessory" || accessoryType == "frontAccessory" || accessoryType == "waistAccessory")
		{
			mesh.LocalPosition = new Vector3(0, -6.8f, 0);
			accessory.TargetAttachment = PolytorianModel.CharacterAttachmentEnum.LowerTorso;
		}
		else if (accessoryType == "neckAccessory" || accessoryType == "shoulderAccessory")
		{
			mesh.LocalPosition = new Vector3(0, -8.8f, 0);
			accessory.TargetAttachment = PolytorianModel.CharacterAttachmentEnum.UpperTorso;
		}
		else
		{
			accessory.TargetAttachment = PolytorianModel.CharacterAttachmentEnum.Head;
		}

		return accessory;
	}

	[ScriptMethod]
	public async Task<Tool?> ToolAsync(int id)
	{
		APIStoreItem storeItem = await GetStoreItemCachedAsync(id);

		PTMeshAsset meshAsset = New<PTMeshAsset>();
		meshAsset.AssetID = (uint)id;

		PTImageAsset icon = New<PTImageAsset>();
		icon.ImageID = (uint)id;
		icon.ImageType = ImageTypeEnum.AssetThumbnail;

		Tool tool = New<Tool>(this);
		Mesh mesh = New<Mesh>()!;
		mesh.Size = Vector3.One;
		mesh.Parent = tool;
		mesh.Asset = meshAsset;

		tool.Droppable = false;
		tool.IconImage = icon;

		tool.LocalRotation = Vector3.Zero;
		mesh.LocalRotation = Vector3.Zero;
		tool.Size = new Vector3(0.5f, 0.5f, 0.5f);

		mesh.IncludeOffset = true;
		mesh.Name = "Mesh";
		mesh.CanCollide = false;
		mesh.Anchored = true;
		tool.Name = storeItem.Name;

		mesh.LocalPosition = new Vector3(1f, -7f, -3f);

		return tool;
	}

	private static async Task<APIStoreItem> GetStoreItemCachedAsync(int id)
	{
		if (_storeItemCache.TryGetValue(id, out var cached))
			return cached;

		APIStoreItem storeItem = await PolyAPI.GetStoreItem(id);
		_storeItemCache[id] = storeItem;
		return storeItem;
	}
}
