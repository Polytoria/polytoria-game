// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Godot;
using Polytoria.Client;
using Polytoria.Creator.LSP;
using Polytoria.Creator.Managers;
using Polytoria.Creator.Settings;
using Polytoria.Creator.UI;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Resources;
using Polytoria.Datamodel.Services;
using Polytoria.DocsGen;
using Polytoria.Formats;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Creator;

public partial class CreatorSession : Node, IDisposable
{
	private const string LuauRCContent = @"{
    ""languageMode"": ""nocheck""
}";
	private const string VSCodeSetupContent = @"{
    ""luau-lsp.platform.type"": ""standard"",
    ""luau-lsp.types.definitionFiles"": {
        ""@poly"": ""./.poly/luau/def.d.luau"",
    },
	""files.exclude"": {
        ""**/*.meta"": true
    }
}";

	private static int _worldSessionCounter = 0;

	private Timer _backupTimer = null!;
	private bool _vscodeFileWritten = false;
	private bool _fileScanQueued = false;

	private bool _cleanupQueued = false;

	public string ProjectFolderPath = "";
	public string ProjectFilePath = "";
	public string OldIndexFilePath = "";
	public string InputMapFilePath = "";
	public string PolyFolderPath = "";

	public CreatorProjectMetadata Metadata;
	public InputMapData InputMap = new();

	public FileBrowserTab FileBrowserTab = null!;
	public List<World> OpenedWorlds = [];

	// Index ID - Path
	public Dictionary<string, string> IndexToFile = [];

	public readonly Dictionary<string, string> FileToIndex = [];
	public readonly Dictionary<string, World> WorldPathToRoot = [];

	public LuaCompletionService? LuaCompletion;

	public async Task Init()
	{
		string projectData = File.ReadAllText(ProjectFilePath);

		// Migrate main place to main world
		projectData = projectData.Replace("\"MainPlace\":", "\"MainWorld\":");

		File.WriteAllText(ProjectFilePath, projectData);

		Metadata = PackedFormat.ReadProjectMetadata(projectData);
		FileBrowserTab = FileBrowser.Singleton.Insert(this);
		OldIndexFilePath = Path.GetFullPath(ProjectFolderPath.PathJoin(Globals.ProjectIndexName));
		InputMapFilePath = Path.GetFullPath(ProjectFolderPath.PathJoin(Globals.ProjectInputMapName));
		PolyFolderPath = Path.GetFullPath(ProjectFolderPath.PathJoin(".poly"));

		await SetupFolders();

		List<Task> tsks = [];

		SetupLuaDocs();
		ReadInputMap();

		_backupTimer = new();
		_backupTimer.Timeout += BackupTimeout;
		Globals.Singleton.AddChild(_backupTimer);
		StartBackupTimer();
		StartLuauLSP();
	}

	public override void _Process(double delta)
	{
		if (_fileScanQueued)
		{
			_fileScanQueued = false;
			RescanFolder();
		}
		base._Process(delta);
	}

	private async void StartLuauLSP()
	{
		try
		{
			PT.Print("Starting Luau LSP...");
			LuaCompletion = new(this);
			await LuaCompletion.InitAsync();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			LuaCompletion = null;
		}
	}

	private void MigrateIndexFile()
	{
		if (!File.Exists(OldIndexFilePath)) return;

		PT.Print("Migrating index.json to .meta files...");

		string raw = File.ReadAllText(OldIndexFilePath);
		Dictionary<string, string> legacyIndex;

		try
		{
			legacyIndex = JsonSerializer.Deserialize(raw, ProjectJSONGenerationContext.Default.DictionaryStringString) ?? [];
		}
		catch (Exception ex)
		{
			PT.PrintErr("-> Failed to read legacy index.json: ", ex);
			return;
		}

		foreach ((string id, string relativePath) in legacyIndex)
		{
			string absolutePath = Path.GetFullPath(Path.Join(ProjectFolderPath, relativePath));

			if (!File.Exists(absolutePath))
			{
				PT.Print($"-> Skipping missing: {relativePath}");
				continue;
			}

			string metaPath = PackedFormat.GetMetaPath(absolutePath);

			if (File.Exists(metaPath))
			{
				PT.Print($"-> Skipping already migrated file: {relativePath}");
				continue;
			}

			PT.Print($"-> Writing .meta for: {relativePath}");
			PackedFormat.WriteMetaId(metaPath, id);
		}

		string archivePath = OldIndexFilePath + ".old";
		File.Move(OldIndexFilePath, archivePath);
		PT.Print("Migration complete.");
	}

	private async Task SetupFolders()
	{
		// Clear addon temp folder
		string addonTemp = Path.GetFullPath(PolyFolderPath.PathJoin("addon-temp"));
		if (Directory.Exists(addonTemp))
			Directory.Delete(addonTemp, true);

		if (!Directory.Exists(PolyFolderPath))
			Directory.CreateDirectory(PolyFolderPath);

		MigrateIndexFile();

		PT.Print("Rebuilding index from .meta files...");
		RescanFolder();
	}

	private void SetupLuaDocs()
	{
		string luauRcPath = ProjectFolderPath.PathJoin(".luaurc");
		string luauPath = PolyFolderPath.PathJoin("luau");
		if (!Directory.Exists(luauPath))
		{
			Directory.CreateDirectory(luauPath);
		}

		string versionPath = PolyFolderPath.PathJoin("version");

		if (!File.Exists(versionPath))
		{
			PT.Print("Writing version...");
			File.WriteAllText(versionPath, "");
		}

		PT.Print("Reading version...");
		string versionData = File.ReadAllText(versionPath);

		if (versionData != Globals.AppVersion || Globals.IsInGDEditor)
		{
			PT.Print("Generating doc...");
			LuaDefinitionGenerator.GenerateDocFiles(luauPath);

			PT.Print("Writing doc...");
			File.WriteAllText(versionPath, Globals.AppVersion);
			File.WriteAllText(luauRcPath, LuauRCContent);
		}
	}

	public void Save()
	{
		SaveInputMap();
		SaveFileIndex();
	}

	private void StartBackupTimer()
	{
		float backupInterval = CreatorSettingsService.Instance.Get<float>(CreatorSettingKeys.Backup.BackupInterval);
		_backupTimer.Start(backupInterval * 60f);
	}

	private async void BackupTimeout()
	{
		await SaveBackup();
		StartBackupTimer();
	}

	public World OpenWorld(string filePath, World? worldOverride = null, bool migrateCoords = false)
	{
		filePath = filePath.SanitizePath();
		if (WorldPathToRoot.ContainsKey(filePath)) throw new InvalidOperationException("World already opened");
		string placePath = GlobalizePath(filePath);
		if (!File.Exists(placePath)) throw new FileNotFoundException("World file not found");
		_cleanupQueued = false;
		byte[] worldData = File.ReadAllBytes(placePath);

		World root = worldOverride ?? Globals.LoadInstance<World>();

		root.SessionType = World.SessionTypeEnum.Creator;

		_worldSessionCounter++;
		root.WorldSessionID = _worldSessionCounter;

		DatamodelBridge dmBridge = new();

		root.WorldFilePath = filePath;
		root.LinkedSession = this;

		if (worldOverride == null)
		{
			// Attach new netService if no override
			NetworkService netService = new();
			netService.Attach(root);
			netService.NetworkParent = root;
			netService.NetworkMode = NetworkService.NetworkModeEnum.Creator;
			netService.IsServer = true;
		}

		root.InitEntry();

		root.GDNode.AddChild(dmBridge, true, Node.InternalMode.Back);

		PT.Print("Opening ", filePath);
		PT.Print("-> Full Path: ", placePath);

		Tabs.Singleton.Insert(new Tabs.GameTab() { World = root, Title = placePath.GetFile() });

		OpenedWorlds.Add(root);
		WorldPathToRoot.Add(filePath, root);

		void deletedHandler()
		{
			PT.Print(filePath, " closed");
			root.Deleted -= deletedHandler;
			OpenedWorlds.Remove(root);
			WorldPathToRoot.Remove(filePath);
			dmBridge.QueueFree();

			AddonsManager.UnregisterRoot(root);

			// Debug force garbage collection
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
			GC.WaitForPendingFinalizers();

			if (OpenedWorlds.Count == 0)
			{
				QueueDispose();
			}
		}

		root.Deleted += deletedHandler;

		dmBridge.Attach(root, worldOverride != null);

		root.Root = root;
		root.Setup();

		SyncFileIndex();

		root.IO.IndexToFile = IndexToFile;
		root.IO.FileToIndex = FileToIndex;

		if (worldOverride == null)
		{
			// Load world
			try
			{
				PolyFormat.LoadWorld(root, worldData, migrateCoords);
				root.InvokeReady();
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				CreatorService.Interface.PopupAlert(ex.Message, "World load failure");
			}
		}
		else
		{
			root.InvokeReady();
		}

		RescanFolder();

		// run addons
		AddonsManager.RunAddons(root);

		return root;
	}

	public void QueueDispose()
	{
		_cleanupQueued = true;
		PT.CallDeferred(() =>
		{
			if (_cleanupQueued)
				Dispose();
		});
	}

	public void CloseWorld(string filePath)
	{
		if (!WorldPathToRoot.TryGetValue(filePath, out var root)) return;
		root.ForceDelete();
	}

	public World? OpenMainWorld(World? worldOverride = null)
	{
		return OpenWorld(Metadata.MainWorld, worldOverride);
	}

	public void RescanFolder()
	{
		RefreshFileIndex();

		IndexToFile.Clear();
		FileToIndex.Clear();

		// Rebuild IndexToFile index
		foreach (string metaPath in Directory.EnumerateFiles(ProjectFolderPath, "*" + PackedFormat.MetaExtension, SearchOption.AllDirectories))
		{
			// Skip .poly temp folder
			if (metaPath.StartsWith(PolyFolderPath, StringComparison.OrdinalIgnoreCase)) continue;

			string targetAbsolute = metaPath[..^PackedFormat.MetaExtension.Length];
			if (!File.Exists(targetAbsolute)) continue; // stale meta, ignore

			string? id = PackedFormat.ReadMetaId(metaPath);
			if (string.IsNullOrEmpty(id)) continue;

			string relative = Path.GetRelativePath(ProjectFolderPath, targetAbsolute).SanitizePath();
			IndexToFile[id] = relative;
			FileToIndex[relative] = id;
		}

		FileBrowserTab?.ScanRootFolder();
	}

	public void QueueRescanFolder() => _fileScanQueued = true;

	public void SetMainWorld(string worldPath)
	{
		Metadata.MainWorld = worldPath.SanitizePath();
		CreatorService.Interface.StatusBar?.SetStatus("Set main world to " + worldPath);
		SaveMetadataFile();
	}

	public void SaveMetadataFile()
	{
		File.WriteAllText(ProjectFilePath, JsonSerializer.Serialize(Metadata, ProjectJSONGenerationContext.Default.CreatorProjectMetadata));
	}

	public Task<byte[]> OpenFile(string src)
	{
		src = Path.GetFullPath(Path.Join(ProjectFolderPath, src));
		return File.ReadAllBytesAsync(src);
	}

	public string GlobalizePath(string path)
	{
		string a = Path.GetFullPath(Path.Join(ProjectFolderPath, path)).SanitizePath();
		if (!PathUtils.IsPathInsideDirectory(a, ProjectFolderPath) && a != ProjectFolderPath.SanitizePath()) throw new InvalidOperationException($"Target path is not allowed ({a})");
		return a;
	}

	public string LocalizePath(string path)
	{
		string a = Path.GetRelativePath(ProjectFolderPath, path).SanitizePath();
		if (!PathUtils.IsPathInsideDirectory(a, ProjectFolderPath)) throw new InvalidOperationException("Target path is not allowed");
		return a;
	}

	public FileAttributes GetFileAttributes(string path)
	{
		return File.GetAttributes(GlobalizePath(path));
	}

	public void CreateScript(string atPath)
	{
		if (!atPath.EndsWith(".luau")) return;
		string scriptPath = Path.Join(ProjectFolderPath, atPath).SanitizePath();
		string relativeScriptPath = Path.GetRelativePath(ProjectFolderPath, scriptPath).SanitizePath();

		ScriptTypeEnum scriptType = CreatorService.GetScriptTypeFromPath(scriptPath);

		string scriptName = CreatorService.GetScriptNameFromPath(scriptPath);

		string scriptSource = "";

		if (scriptType == ScriptTypeEnum.Module)
		{
			scriptSource = @"local module = {}

return module";
		}

		string fileBaseFolder = scriptPath.GetBaseDir();

		if (!Directory.Exists(fileBaseFolder))
		{
			Directory.CreateDirectory(fileBaseFolder);
		}

		File.WriteAllText(scriptPath, scriptSource);

		if (CreatorService.Interface.PendingCreateScriptAt != null)
		{
			if (World.Current == null) return;
			World currentGame = World.Current;
			Script? scriptToCreate = null;
			switch (scriptType)
			{
				case ScriptTypeEnum.Server:
					scriptToCreate = currentGame.New<ServerScript>();
					break;
				case ScriptTypeEnum.Client:
					scriptToCreate = currentGame.New<ClientScript>();
					break;
				case ScriptTypeEnum.Module:
					scriptToCreate = currentGame.New<ModuleScript>();
					break;
			}
			if (scriptToCreate != null)
			{
				scriptToCreate.Name = scriptName.ToPascalCase();
				scriptToCreate.LinkedScript = currentGame.Assets.GetFileLinkByPath(relativeScriptPath);
				scriptToCreate.Parent = CreatorService.Interface.PendingCreateScriptAt;
				currentGame.CreatorContext.Selections.DeselectAll();
				currentGame.CreatorContext.Selections.Select(scriptToCreate);
			}
			CreatorService.Interface.PendingCreateScriptAt = null;
		}


		// Add auto select
		FileBrowserTab.BrowserTree.AutoSelects.Clear();
		FileBrowserTab.BrowserTree.AutoSelects.Add(relativeScriptPath);
		RescanFolder();
	}

	public async Task CreateWorld(string atPath)
	{
		atPath = atPath.SanitizePath();
		string globalized = GlobalizePath(atPath);

		if (!atPath.EndsWith(".poly"))
		{
			throw new InvalidOperationException("World file must end with .poly extension");
		}

		if (File.Exists(globalized))
		{
			throw new Exception("The world already exists");
		}

		await File.WriteAllBytesAsync(globalized, []);
		FileBrowserTab.AutoSelects.Add(atPath);
		RescanFolder();
	}

	public async Task CreateFolder(string atPath)
	{
		atPath = atPath.SanitizePath();
		string globalized = GlobalizePath(atPath);

		if (Directory.Exists(globalized))
		{
			throw new Exception("Folder already exists");
		}

		Directory.CreateDirectory(globalized);
		FileBrowserTab.AutoSelects.Add(atPath + "/");
		RescanFolder();
	}

	public void RemoveFile(string src, bool toRecycleBin = false)
	{
		if (src == Globals.ProjectMetaFileName) throw new InvalidOperationException("Cannot delete the project metadata file");
		if (src == Globals.ProjectInputMapName) throw new InvalidOperationException("Cannot delete the input map");
		if (World.Current != null && World.Current.WorldFilePath == src) throw new InvalidOperationException("Cannot delete currently opened world");
		if (src == "") throw new InvalidOperationException("Cannot delete the root folder");

		string absoluteSrc = GlobalizePath(src);

		if (toRecycleBin)
		{
			Error err = OS.MoveToTrash(absoluteSrc);
			if (err != Error.Ok)
				PT.PrintWarn($"Failed to move to recycle bin: {absoluteSrc}");

			// Move the .meta file to trash if it exists
			string metaPath = PackedFormat.GetMetaPath(absoluteSrc);
			if (File.Exists(metaPath))
			{
				err = OS.MoveToTrash(metaPath);
				if (err != Error.Ok)
					PT.PrintWarn($"Failed to move .meta to recycle bin: {metaPath}");
			}
		}
		else if (File.GetAttributes(absoluteSrc) == FileAttributes.Directory)
		{
			Directory.Delete(absoluteSrc, true);
		}
		else
		{
			File.Delete(absoluteSrc);

			// Delete the .meta file if it exists
			string metaPath = PackedFormat.GetMetaPath(absoluteSrc);
			if (File.Exists(metaPath))
				File.Delete(metaPath);
		}

		QueueRescanFolder();
	}

	public void RenameFile(string src, string renameTo)
	{
		if (src == Globals.ProjectMetaFileName) return;
		if (!FileExists(src)) return;
		string srcRelative = src.SanitizePath();
		src = GlobalizePath(src);

		string newPathRelative;
		string newPath;

		if (File.GetAttributes(src) == FileAttributes.Directory)
		{
			string parentDir = srcRelative.TrimEnd('/').GetBaseDir();
			newPathRelative = parentDir.PathJoin(renameTo).SanitizePath() + "/";
			newPath = GlobalizePath(newPathRelative);

			Directory.Move(src, newPath);
			UpdateDirectoryIndex(srcRelative, newPathRelative);
		}
		else
		{
			newPathRelative = srcRelative.GetBaseDir().PathJoin(renameTo).SanitizePath();
			newPath = GlobalizePath(newPathRelative);

			File.Move(src, newPath);
		}

		if (FileToIndex.TryGetValue(srcRelative, out string? id))
		{
			PT.Print("Renaming change index");
			IndexToFile[id] = newPathRelative;
			SyncFileIndex();
		}

		FileBrowserTab.AutoSelects.Add(newPathRelative);
		QueueRescanFolder();
	}

	public bool FileExists(string src)
	{
		src = GlobalizePath(src);
		if (src.EndsWith('/'))
		{
			return Directory.Exists(src);
		}
		return File.Exists(src);
	}

	public string? MoveFile(string src, string dest)
	{
		string srcRelative = src.SanitizePath();
		string destRelative = dest.SanitizePath();
		string absoluteSrc = GlobalizePath(src);
		string absoluteDest = GlobalizePath(dest);

		string srcFile = absoluteSrc.GetFile();
		string? finalDest = null;

		FileAttributes srcA = File.GetAttributes(absoluteSrc);
		FileAttributes destA = File.GetAttributes(absoluteDest);

		if (srcA == FileAttributes.Directory && destA == FileAttributes.Directory)
		{
			DirectoryInfo srcInfo = new(absoluteSrc);
			finalDest = (Path.Join(destRelative, srcInfo.Name) + "/").SanitizePath();
			Directory.Move(absoluteSrc, Path.Join(absoluteDest, srcInfo.Name));

			UpdateDirectoryIndex(srcRelative, finalDest);
		}

		if (srcA != FileAttributes.Directory && destA == FileAttributes.Directory)
		{
			finalDest = Path.Join(destRelative, srcFile).SanitizePath();
			string newAbsolute = Path.Join(absoluteDest, srcFile);
			File.Move(absoluteSrc, newAbsolute);

			// Move the slibing .meta file
			string oldMeta = PackedFormat.GetMetaPath(absoluteSrc);
			string newMeta = PackedFormat.GetMetaPath(newAbsolute);
			if (File.Exists(oldMeta))
				File.Move(oldMeta, newMeta);
		}

		if (finalDest != null)
		{
			string key = IndexToFile.FirstOrDefault(x => x.Value == srcRelative).Key;
			if (key != null)
			{
				PT.Print("Updating index: ", key);
				IndexToFile[key] = finalDest;
			}
			SyncFileIndex();
		}

		QueueRescanFolder();
		return finalDest;
	}


	private void UpdateDirectoryIndex(string oldDirPath, string newDirPath)
	{
		// Ensure paths end with separator
		oldDirPath = oldDirPath.TrimEnd('/') + "/";
		newDirPath = newDirPath.TrimEnd('/') + "/";

		List<KeyValuePair<string, string>> filesToUpdate = [.. IndexToFile.Where(x => x.Value.StartsWith(oldDirPath))];

		foreach ((string key, string val) in filesToUpdate)
		{
			string relativePath = val[oldDirPath.Length..];
			string newPath = newDirPath + relativePath;

			PT.Print($"Updating subfile index: {key} from {val} to {newPath}");
			IndexToFile[key] = newPath.SanitizePath();
		}

		SyncFileIndex();
	}

	public async void SaveModel(Instance src, string dest)
	{
		try
		{
			PT.Print("Writing model to ", dest);
			bool prevEditPref = src.EditableChildren;
			src.EditableChildren = false;

			FileLinkAsset? link = src.Root.Assets.GetFileLinkByPath(dest);
			dest = GlobalizePath(dest);

			string baseDir = dest.GetBaseDir();

			if (!Directory.Exists(baseDir))
			{
				Directory.CreateDirectory(baseDir);
			}

			src.LinkedModel = link;

			var data = PolyFormat.SaveModel(src);
			PolyFormat.SaveModelToFile(src, dest);

			foreach (Instance item in src.GetDescendants())
			{
				item.ModelRoot ??= src;
			}

			// Inject model data
			foreach (World root in OpenedWorlds)
			{
				foreach ((_, FileLinkAsset fileLink) in root.Assets.FileLinks.ToArray())
				{
					if (fileLink.LinkedPath == link.LinkedPath)
					{
						foreach (NetworkedObject linkedWith in fileLink.LinkedTo.ToArray())
						{
							if (linkedWith is Instance i && i.LinkedModel != null && i != src)
							{
								PolyFormat.InjectModelData(data, i);
								i.LinkedModel = src.LinkedModel;
							}
						}
					}
				}
			}

			SyncFileIndex();

			src.EditableChildren = prevEditPref;
			PT.Print("Success! ", src.LinkedModel.Name);

			QueueRescanFolder();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message, "Failed creating model");
		}
	}

	public async Task<Instance?> InsertModel(string path, Instance parent)
	{
		try
		{
			PT.Print("Inserting model from ", path);
			string pathRelative = path;
			path = GlobalizePath(path);
			Instance? i = PolyFormat.LoadModelFromFile(parent.Root, path, parent);
			if (i != null)
			{
				i.LinkedModel = i.Root.Assets.GetFileLinkByPath(pathRelative);
				parent.Root.CreatorContext.Selections.SelectOnly(i);

				return i;
			}
			return null;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message, "Failed inserting model");
			return null;
		}
	}

	public void ToggleCompressed(string path)
	{
		path = GlobalizePath(path);
		bool isCompressed = PolyFormat.IsPolyFileCompressed(path);
		if (isCompressed)
		{
			FileSwitchToUncompressed(path);
		}
		else
		{
			FileSwitchToCompressed(path);
		}
	}

	public static void FileSwitchToCompressed(string path)
	{
		string ext = path.GetExtension();
		if (ext == "poly" || ext == "model")
		{
			PT.Print(path, " switching to compressed...");
			byte[] data = File.ReadAllBytes(path);
			byte[] final = PolyFormat.CompressPolyContent(data);
			File.WriteAllBytes(path, final);
		}
	}

	public static void FileSwitchToUncompressed(string path)
	{
		string ext = path.GetExtension();
		if (ext == "poly" || ext == "model")
		{
			PT.Print(path, " switching to uncompressed...");
			byte[] data = File.ReadAllBytes(path);
			byte[] final = PolyFormat.DecompressPolyContent(data);
			File.WriteAllBytes(path, final);
		}
	}

	public void RunScript(string path)
	{
		if (World.Current == null) { PT.Print("World current is null, did not run script"); return; }
		Script s = new() { Root = World.Current };
		path = GlobalizePath(path);
		s.Source = File.ReadAllText(path);
		s.PermissionFlags = Scripting.ScriptPermissionFlags.IORead | Scripting.ScriptPermissionFlags.IOWrite;
		s.Parent = World.Current.TemporaryContainer;
		s.Run();
	}

	/// <summary>
	/// Sync file index to all game's IO
	/// </summary>
	internal void SyncFileIndex()
	{
		FileToIndex.Clear();
		foreach (KeyValuePair<string, string> item in IndexToFile)
		{
			FileToIndex[item.Value] = item.Key;
		}
		foreach (World item in OpenedWorlds)
		{
			item.IO.IndexToFile = IndexToFile;
			item.IO.FileToIndex = FileToIndex;
		}
	}

	internal void RefreshFileIndex()
	{
		List<string> staleIds = [];

		// Clear orphan .meta files
		foreach (string metaPath in Directory.EnumerateFiles(ProjectFolderPath, "*" + PackedFormat.MetaExtension, SearchOption.AllDirectories))
		{
			// Ignore .poly folder
			if (metaPath.StartsWith(PolyFolderPath, StringComparison.OrdinalIgnoreCase)) continue;

			string targetPath = metaPath[..^PackedFormat.MetaExtension.Length];
			if (!File.Exists(targetPath))
				File.Delete(metaPath);
		}

		// Clear files that doesn't exist anymore
		foreach (var item in IndexToFile)
		{
			string absolutePath = GlobalizePath(item.Value);

			if (!File.Exists(absolutePath))
			{
				// file gone, queue stale entry for removal
				staleIds.Add(item.Key);

				// Clean up any orphaned .meta file
				string orphanMeta = PackedFormat.GetMetaPath(absolutePath);
				if (File.Exists(orphanMeta))
					File.Delete(orphanMeta);

				continue;
			}

			// Write .meta file alongside the actual file
			string metaPath = PackedFormat.GetMetaPath(absolutePath);
			PackedFormat.WriteMetaId(metaPath, item.Key);
		}

		// Remove stale entries
		foreach (string id in staleIds)
			IndexToFile.Remove(id);
	}

	public void SaveFileIndex()
	{
		SyncFileIndex();
		RefreshFileIndex();
	}

	internal void ReadInputMap()
	{
		if (!File.Exists(InputMapFilePath))
		{
			PT.Print("Writing input map...");

			// Write default input map
			InputActionAxis h = InputMap.BindAxis("Horizontal");
			h.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.D });
			h.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.Right });
			h.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.GamepadAxisLeftX });
			h.Negative.AddButton(new() { KeyCode = Enums.KeyCodeEnum.A });
			h.Negative.AddButton(new() { KeyCode = Enums.KeyCodeEnum.Left });

			InputActionAxis v = InputMap.BindAxis("Vertical");
			v.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.W });
			v.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.Up });
			v.Positive.AddButton(new() { KeyCode = Enums.KeyCodeEnum.GamepadAxisLeftY });
			v.Negative.AddButton(new() { KeyCode = Enums.KeyCodeEnum.S });
			v.Negative.AddButton(new() { KeyCode = Enums.KeyCodeEnum.Down });


			SaveInputMap();
		}
		else
		{
			PT.Print("Reading input map... ", InputMapFilePath);
			string inputData = File.ReadAllText(InputMapFilePath);
			try
			{
				InputMap = InputMapData.LoadFromString(inputData);
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}
	}

	public void SaveInputMap()
	{
		PT.Print("Writing input map... ", InputMapFilePath);
		string jsonContent = InputMap.SaveToString();
		File.WriteAllText(InputMapFilePath, jsonContent);
	}

	public async Task SaveBackup()
	{
		CreatorService.Interface.StatusBar?.SetStatus("Backing up world...");

		string backupFolderPath = PolyFolderPath.PathJoin("backups");
		if (!Directory.Exists(backupFolderPath))
		{
			Directory.CreateDirectory(backupFolderPath);
		}

		int maxCount = CreatorSettingsService.Instance.Get<int>(CreatorSettingKeys.Backup.MaxBackupCount);

		// Delete oldest folder
		List<DirectoryInfo> backupFolders = [.. Directory.GetDirectories(backupFolderPath)
			.Select(path => new DirectoryInfo(path))
			.OrderBy(dir => dir.CreationTime)];

		if (backupFolders.Count >= maxCount)
		{
			DirectoryInfo oldestFolder = backupFolders.First();
			Directory.Delete(oldestFolder.FullName, true);
		}

		DateTime time = DateTime.Now;
		string formattedTime = time.ToString("yyyy-MM-dd-hhmmss");

		string snapshotFolder = backupFolderPath.PathJoin(formattedTime);
		if (!Directory.Exists(snapshotFolder))
		{
			Directory.CreateDirectory(snapshotFolder);
		}

		foreach (World game in OpenedWorlds)
		{
			if (game.WorldFilePath == null) { PT.PrintWarn("Skipping game instance, no linked world file path"); continue; }
			string fpath = game.WorldFilePath;
			string writeTo = snapshotFolder + "/" + fpath;
			string baseDir = writeTo.GetBaseDir();

			if (!Directory.Exists(baseDir))
			{
				Directory.CreateDirectory(baseDir);
			}

			PolyFormat.SaveWorldToFile(game, writeTo);
		}
		CreatorService.Interface.StatusBar?.SetStatus("World backed up!");
	}

	public void CreateVSCodeConfig()
	{
		if (_vscodeFileWritten) return;
		string p = ProjectFolderPath.PathJoin(".vscode");
		if (!Directory.Exists(p))
		{
			Directory.CreateDirectory(p);
		}
		else
		{
			return;
		}

		File.WriteAllText(p.PathJoin("settings.json"), VSCodeSetupContent);
		_vscodeFileWritten = true;
	}

	public new void Dispose()
	{
		LuaCompletion?.Shutdown();

		Tabs.Singleton.CloseTabsOfSession(this);

		QueueFree();
		(this as Node).Dispose();

		GC.SuppressFinalize(this);
	}
}
