// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI;
using Polytoria.Creator.Utils;
using Polytoria.Creator.Settings;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Formats;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Polytoria.Creator.Managers;

public static class PublishManager
{
	public static async Task PublishProject(string projectPath, int placeID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			var metadata = PackedFormat.ReadProjectMetadata(File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName)));

			var packed = await PackedFormat.PackProject(projectPath, loadOverlay.CreateProgressReporter("Publishing world"));

			loadOverlay?.SetStatus("Uploading now...");
			CreatorPublishResponse publishRes = await PolyCreatorAPI.UploadWorld(packed, placeID, metadata.MainWorld);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("World published");
			loadOverlay?.Hide();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
			loadOverlay?.Hide();
		}
	}

	public static async Task PublishModel(Instance target, int modelID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			byte[] packed = await PackedFormat.PackModel(target, loadOverlay.CreateProgressReporter("Publishing model"));

			CreatorService.Interface.LoadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await PolyCreatorAPI.UploadModel(packed, modelID);
			CreatorService.Interface.LoadOverlay?.Hide();

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Model published");
			loadOverlay?.Hide();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
			loadOverlay?.Hide();
		}
	}

	/*
	public static async Task PublishAddon(ServerScript target, int placeID = 0)
	{
		CreatorService.Interface.LoadOverlay?.SetTitle("Publishing addon...");
		CreatorService.Interface.LoadOverlay?.SetStatus("Packing addon...");
		CreatorService.Interface.LoadOverlay?.Show();

		byte[] packed = await PackedFormat.PackAddon(target);

		CreatorService.Interface.LoadOverlay?.SetStatus("Uploading now...");

		CreatorPublishResponse publishRes = await PolyCreatorAPI.UploadAddon(packed, placeID);
		CreatorService.Interface.LoadOverlay?.Hide();

		if (CreatorSettings.Singleton.GetSetting<bool>("Creator.OpenWebAfterPublish")!)
			OS.ShellOpen(publishRes.Link);
		CreatorService.Interface.StatusBar?.SetStatus("Addon published");
	}
	*/
}
