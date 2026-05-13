// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Services;
using Polytoria.Formats;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Polytoria.Creator.Managers.ProjectManager;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.Managers;

public static class ProjectManager
{
	private const string RecentsPath = "user://creator/recents";
	private const string ProjectTemplatesPath = "res://modules/world-templates/";
	private const string GitIgnoreContent = "# Polytoria specific ignores\n.poly/\n";

	public static async Task<RecentData[]> GetRecents(bool loadData = true)
	{
		string recentsPath = ProjectSettings.GlobalizePath(RecentsPath);
		if (File.Exists(recentsPath))
		{
			string raw = File.ReadAllText(recentsPath);
			RecentData[] data = JsonSerializer.Deserialize(raw, RecentsFileGenerationContext.Default.RecentDataArray) ?? [];

			List<RecentData> finalData = [];
			List<Task> tasks = [];

			foreach (RecentData r in data)
			{
				if (!Directory.Exists(r.FolderPath)) continue;

				if (loadData)
				{
					string projectMetaFile = Path.GetFullPath(Path.Join(r.FolderPath, Globals.ProjectMetaFileName));
					if (!File.Exists(projectMetaFile)) continue;

					tasks.Add(Task.Run(async () =>
					{
						string projectTxt = File.ReadAllText(projectMetaFile);
						CreatorProjectMetadata metadata = JsonSerializer.Deserialize(projectTxt, ProjectJSONGenerationContext.Default.CreatorProjectMetadata);

						finalData.Add(new()
						{
							PlaceName = metadata.ProjectName,
							IconID = metadata.IconID,
							FolderPath = r.FolderPath,
							LastOpened = r.LastOpened
						});
					}));
				}
				else
				{
					finalData.Add(r);
				}
			}

			await Task.WhenAll(tasks);

			return [.. finalData.OrderByDescending(x => x.LastOpened)];
		}
		else
		{
			return [];
		}
	}

	public static async Task AddToRecents(string folderPath)
	{
		string recentsPath = ProjectSettings.GlobalizePath(RecentsPath);
		List<RecentData> existing = [.. await GetRecents(false)];

		existing.RemoveAll(x => x.FolderPath == folderPath);

		existing.Add(new()
		{
			FolderPath = folderPath,
			LastOpened = DateTime.Now,
		});

		File.WriteAllText(recentsPath, JsonSerializer.Serialize([.. existing], RecentsFileGenerationContext.Default.RecentDataArray));
	}

	public static async Task RemoveFromRecents(string folderPath)
	{
		string recentsPath = ProjectSettings.GlobalizePath(RecentsPath);
		List<RecentData> existing = [.. await GetRecents(false)];

		existing.RemoveAll(x => x.FolderPath == folderPath);
		File.WriteAllText(recentsPath, JsonSerializer.Serialize([.. existing], RecentsFileGenerationContext.Default.RecentDataArray));
	}

	public static async Task NewProject(string destFolder, CreatorProjectMetadata metadata, bool createFromTemplate = false)
	{
		string projectMainPlacePath = Path.GetFullPath(Path.Join(destFolder, metadata.MainWorld));
		string projectMetaPath = Path.GetFullPath(Path.Join(destFolder, Globals.ProjectMetaFileName));
		string projectGitIgnore = Path.GetFullPath(Path.Join(destFolder, ".gitignore"));
		string scriptsPath = Path.GetFullPath(Path.Join(destFolder, "scripts"));
		string serverPath = Path.GetFullPath(Path.Join(scriptsPath, "server"));
		string clientPath = Path.GetFullPath(Path.Join(scriptsPath, "client"));
		string modulePath = Path.GetFullPath(Path.Join(scriptsPath, "modules"));

		CreatorService.Interface.LoadOverlay?.SetTitle("Creating new project");
		CreatorService.Interface.LoadOverlay?.SetStatus("Creating...");
		CreatorService.Interface.LoadOverlay?.Show();

		File.WriteAllText(projectMetaPath, JsonSerializer.Serialize(metadata, ProjectJSONGenerationContext.Default.CreatorProjectMetadata));
		File.WriteAllText(projectGitIgnore, GitIgnoreContent);
		if (!createFromTemplate)
		{
			File.WriteAllBytes(projectMainPlacePath, []);
		}

		if (!Directory.Exists(scriptsPath))
		{
			Directory.CreateDirectory(scriptsPath);
		}
		if (!Directory.Exists(serverPath))
		{
			Directory.CreateDirectory(serverPath);
		}
		if (!Directory.Exists(clientPath))
		{
			Directory.CreateDirectory(clientPath);
		}
		if (!Directory.Exists(modulePath))
		{
			Directory.CreateDirectory(modulePath);
		}

		CreatorService.Interface.LoadOverlay?.SetStatus("Opening Project...");
		await CreatorService.Singleton.CreateNewSession(projectMetaPath);
		CreatorService.Interface.LoadOverlay?.Hide();
	}

	public static async Task NewProjectFromTemplate(string destFolder, string templateFolderPath, CreatorProjectMetadata metadata)
	{
		if (!Directory.Exists(destFolder))
		{
			Directory.CreateDirectory(destFolder);
		}
		using DirAccess dir = DirAccess.Open(templateFolderPath) ?? throw new DirectoryNotFoundException($"Template folder not found: {templateFolderPath}");

		CopyResourceDirRecursive(templateFolderPath, destFolder);
		await NewProject(destFolder, new() { MainWorld = "main.poly", ProjectName = metadata.ProjectName }, true);
	}

	private static void CopyResourceDirRecursive(string sourceDir, string destDir)
	{
		using DirAccess dir = DirAccess.Open(sourceDir);
		if (dir == null) return;

		dir.ListDirBegin();
		string fileName = dir.GetNext();

		while (fileName != "")
		{
			if (fileName == "." || fileName == "..")
			{
				fileName = dir.GetNext();
				continue;
			}

			string sourcePath = $"{sourceDir}/{fileName}";
			string destPath = Path.Combine(destDir, fileName);

			if (dir.CurrentIsDir())
			{
				// Create subdirectory and recurse
				Directory.CreateDirectory(destPath);
				CopyResourceDirRecursive(sourcePath, destPath);
			}
			else
			{
				string extension = sourcePath.GetExtension();

				// Ignore template files
				if (extension == "import" || extension == "png" || extension == "json")
				{
					fileName = dir.GetNext();
					continue;
				}

				// Copy file
				byte[] fileData = Godot.FileAccess.GetFileAsBytes(sourcePath);
				File.WriteAllBytes(destPath, fileData);
			}

			fileName = dir.GetNext();
		}

		dir.ListDirEnd();
	}


	public static async Task ImportLegacyWorld(string placePath, string destFolder, CreatorProjectMetadata metadata)
	{
		Stopwatch sw = new();
		sw.Start();
		destFolder = Path.GetFullPath(destFolder);

		string projectMainWorldPath = Path.GetFullPath(Path.Join(destFolder, metadata.MainWorld));
		string projectMetaPath = Path.GetFullPath(Path.Join(destFolder, Globals.ProjectMetaFileName));
		string scriptsPath = Path.GetFullPath(Path.Join(destFolder, "scripts"));
		string serverPath = Path.GetFullPath(Path.Join(scriptsPath, "server"));
		string clientPath = Path.GetFullPath(Path.Join(scriptsPath, "client"));
		string modulePath = Path.GetFullPath(Path.Join(scriptsPath, "modules"));

		CreatorService.Interface.LoadOverlay?.SetTitle("Importing Legacy World...");
		CreatorService.Interface.LoadOverlay?.SetStatus("Loading legacy world...");
		CreatorService.Interface.LoadOverlay?.Show();

		File.WriteAllText(projectMetaPath, JsonSerializer.Serialize(metadata, ProjectJSONGenerationContext.Default.CreatorProjectMetadata));

		if (!Directory.Exists(scriptsPath))
		{
			Directory.CreateDirectory(scriptsPath);
		}
		if (!Directory.Exists(serverPath))
		{
			Directory.CreateDirectory(serverPath);
		}
		if (!Directory.Exists(clientPath))
		{
			Directory.CreateDirectory(clientPath);
		}
		if (!Directory.Exists(modulePath))
		{
			Directory.CreateDirectory(modulePath);
		}

		World3D world3D = new();

		SubViewport tempViewport = new()
		{
			RenderTargetClearMode = SubViewport.ClearMode.Never,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
			World3D = world3D
		};

		World root = Globals.LoadInstance<World>();
		root.SessionType = World.SessionTypeEnum.Creator;
		root.World3D = world3D;

		NetworkService netService = new();
		netService.Attach(root);

		netService.NetworkMode = NetworkService.NetworkModeEnum.Creator;
		netService.IsServer = true;

		CreatorService.Singleton.AddChild(tempViewport);
		tempViewport.AddChild(root.GDNode);

		root.Root = root;
		root.InitEntry();
		root.Setup();

		await XmlFormat.LoadFile(root, placePath);

		Dictionary<string, string> addedScripts = [];
		Dictionary<string, int> nameCounters = [];
		Dictionary<string, string> sourceToPath = [];

		Dictionary<string, string> indexToFile = [];

		List<Script> scripts = [];

		foreach (Instance item in root.GetDescendants())
		{
			if (item is Script s)
			{
				scripts.Add(s);
			}
		}

		CreatorService.Interface.LoadOverlay?.SetMaxProgress(scripts.Count);

		int i = 0;

		foreach (Script s in scripts)
		{
			string targetFolder;
			string targetName;
			string baseName = s.Name;
			if (s is ServerScript)
			{
				targetFolder = serverPath;
				targetName = $"{baseName}.server.luau";
			}
			else if (s is ClientScript)
			{
				targetFolder = clientPath;
				targetName = $"{baseName}.client.luau";
			}
			else
			{
				targetFolder = modulePath;
				targetName = $"{baseName}.luau";
			}

			// Check if this source already exists
			if (sourceToPath.TryGetValue(s.Source, out string? existingPath))
			{
				// Reuse the existing file path
				s.LinkedScript = root.Assets.GetFileLinkByPath(existingPath);
				i++;
				continue;
			}

			string fullKey = $"{targetFolder}:{targetName}";
			if (addedScripts.ContainsKey(fullKey))
			{
				if (!nameCounters.TryGetValue(fullKey, out int value))
				{
					value = 1;
					nameCounters[fullKey] = value;
				}
				nameCounters[fullKey] = ++value;
				if (s is ServerScript)
				{
					targetName = $"{baseName}{value}.server.luau";
				}
				else if (s is ClientScript)
				{
					targetName = $"{baseName}{nameCounters[fullKey]}.client.luau";
				}
				else
				{
					targetName = $"{baseName}{nameCounters[fullKey]}.luau";
				}
			}
			string targetFile = Path.GetFullPath(Path.Join(targetFolder, targetName));

			string relativeScriptPath = Path.GetRelativePath(destFolder, targetFile).SanitizePath();

			CreatorService.Interface.LoadOverlay?.SetStatus($"Importing {targetName}");
			CreatorService.Interface.LoadOverlay?.SetProgress(i);

			File.WriteAllText(targetFile, s.Source);

			// Path Mapping
			sourceToPath[s.Source] = relativeScriptPath;

			s.LinkedScript = root.Assets.GetFileLinkByPath(relativeScriptPath);
			addedScripts[fullKey] = targetFile;

			indexToFile[s.LinkedScript.LinkedID] = relativeScriptPath;

			i++;
		}

		CreatorService.Interface.LoadOverlay?.SetStatus("Saving index...");
		foreach ((string id, string relativePath) in indexToFile)
		{
			string absolutePath = Path.GetFullPath(Path.Join(destFolder, relativePath));
			string metaPath = absolutePath + PackedFormat.MetaExtension;
			PackedFormat.WriteMetaId(metaPath, id);
		}

		CreatorService.Interface.LoadOverlay?.SetStatus("Saving world...");
		PolyFormat.SaveWorldToFile(root, projectMainWorldPath);

		root.ForceDelete();

		tempViewport.QueueFree();

		CreatorService.Interface.LoadOverlay?.Hide();
		await CreatorService.Singleton.CreateNewSession(projectMetaPath);
		PT.Print("Legacy conversion took ", sw.ElapsedMilliseconds, "ms");
	}

	public static async Task InitializeGit(string projPath)
	{
		await RunGitCommand(projPath, "init");
		await RunGitCommand(projPath, "add .");
		await RunGitCommand(projPath, "commit -m \"Initial commit\"");
	}

	private static async Task RunGitCommand(string workingDirectory, string arguments)
	{
		Process p = new();
		p.StartInfo.FileName = "git";
		p.StartInfo.Arguments = arguments;
		p.StartInfo.UseShellExecute = true;
		p.StartInfo.WorkingDirectory = workingDirectory;
		p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
		p.Start();
		await p.WaitForExitAsync();
	}


	public static string[] GetProjectTemplatePaths()
	{
		List<string> p = [];
		foreach (string item in DirAccess.GetDirectoriesAt(ProjectTemplatesPath))
		{
			p.Add(ProjectTemplatesPath.PathJoin(item));
		}
		return [.. p];
	}

	public struct RecentData
	{
		[JsonInclude] public string FolderPath;
		[JsonInclude] public DateTime LastOpened;
		[JsonIgnore] public string PlaceName;
		[JsonIgnore] public int? IconID;
	}

	public struct TemplateProjectJSON
	{
		public string Name { get; set; }
		public string Description { get; set; }
	}
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RecentData))]
[JsonSerializable(typeof(RecentData[]))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal partial class RecentsFileGenerationContext : JsonSerializerContext { }

[JsonSerializable(typeof(TemplateProjectJSON))]
[JsonSerializable(typeof(string))]
internal partial class TemplateProjectJSONGenerationContext : JsonSerializerContext { }
