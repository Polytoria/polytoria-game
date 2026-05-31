// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings.Appliers;
using Polytoria.Creator.Managers;
using Polytoria.Creator.Settings;
using Polytoria.Creator.Utils;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Shared.Settings;
using System.Collections.Generic;
using System.IO;

namespace Polytoria.Creator;

public partial class CreatorEntry : Node
{
	public const int CreatorPort = 24220;

	public async override void _EnterTree()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? launchToken);

		CreatorService creatorService = new();
		AddChild(creatorService);

		CreatorSettingsService creatorSettingsService = new()
		{
			Name = "CreatorSettingsService"
		};
		AddChild(creatorSettingsService, true, InternalMode.Front);
		creatorSettingsService.Init();

		AssetLoader.Singleton.MaxConcurrentRequests = creatorSettingsService.Get<int>(SharedSettingKeys.Advanced.AssetQueue);

		creatorSettingsService.AddChild(new GraphicsSettingsApplier { Name = GraphicsSettingsApplier.NodeName, Settings = creatorSettingsService }, true, InternalMode.Front);

		GetViewport().GuiEmbedSubwindows = true;

		// Open project
		cmdargs.TryGetValue("proj", out string? creatorFilePath);
		if (creatorFilePath != null)
		{
			_ = CreatorService.Singleton.CreateNewSession(creatorFilePath);
		}

		// Import legacy world cmd arguments
		cmdargs.TryGetValue("liin", out string? legacyImportIn);
		cmdargs.TryGetValue("liout", out string? legacyImportOut);

		if (legacyImportIn != null && legacyImportOut != null)
		{
			_ = ProjectManager.ImportLegacyWorld(legacyImportIn, legacyImportOut, new() { MainWorld = "main.poly", ProjectName = new DirectoryInfo(legacyImportOut).Name });
		}

		// Login creator with token
		if (launchToken != null)
		{
			await PolyCreatorAPI.LoginWithToken(launchToken);
		}
	}
}
