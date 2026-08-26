// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#if CREATOR
using Godot;
using Polytoria.Schemas.Progress;
using Polytoria.Datamodel.Resources;
using Polytoria.Creator.Managers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;
using static Polytoria.Creator.Managers.AddonsManager;
#endif
using Polytoria.Datamodel;
using Polytoria.Datamodel.Data;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Formats;

public static partial class PackedFormat
{
	internal const string MetaExtension = ".meta";
	private const int MaxParallelism = 10;

	internal static string GetStamp()
	{
		return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString();
	}

#if CREATOR
	internal static string GetMetaPath(string absoluteFilePath) => absoluteFilePath + MetaExtension;

	internal static string? ReadMetaId(string metaPath)
	{
		if (!File.Exists(metaPath)) return null;
		try
		{
			string json = File.ReadAllText(metaPath);
			var dict = JsonSerializer.Deserialize(json, ProjectJSONGenerationContext.Default.DictionaryStringString);
			return dict != null && dict.TryGetValue("id", out string? id) ? id : null;
		}
		catch
		{
			return null;
		}
	}

	internal static void WriteMetaId(string metaPath, string id)
	{
		Dictionary<string, string> dict = new() { ["id"] = id };
		File.WriteAllText(metaPath, JsonSerializer.Serialize(dict, ProjectJSONGenerationContext.Default.DictionaryStringString));
	}

	public static async Task<byte[]> PackProject(string projectPath, IProgress<LoadOverlayProgress>? progress = null)
	{
		using MemoryStream stream = new();
		using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
		{
			await PackProjectToArchive(projectPath, archive, progress);
		}
		return stream.ToArray();
	}

	public static async Task<byte[]> PackModel(Instance model, IProgress<LoadOverlayProgress>? progress = null)
	{
		using MemoryStream stream = new();
		using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
		{
			await PackModelToArchive(model, archive, progress);
		}
		return stream.ToArray();
	}

	public static async Task PackProjectToArchive(string projectPath, ZipArchive archive, IProgress<LoadOverlayProgress>? progress = null)
	{
		string metaJsonPath = projectPath.PathJoin(Globals.ProjectMetaFileName);
		string inputJsonPath = projectPath.PathJoin(Globals.ProjectInputMapName);

		progress?.Report(new()
		{
			Status = "Packing project...",
			Current = 0
		});

		// Pack input
		ZipArchiveEntry inputEntry = archive.CreateEntry("input.json");
		using (Stream inputStream = inputEntry.Open())
		using (StreamWriter writer = new(inputStream))
		{
			writer.Write(File.ReadAllText(inputJsonPath));
		}

		ZipArchiveEntry metaEntry = archive.CreateEntry("meta.json");
		using (Stream inputStream = metaEntry.Open())
		using (StreamWriter writer = new(inputStream))
		{
			writer.Write(File.ReadAllText(metaJsonPath));
		}

		Dictionary<string, string> indexToFile = [];

		string[] allFiles = Directory.GetFiles(projectPath, "*", SearchOption.AllDirectories);
		foreach (string file in allFiles)
		{
			// Skip files inside the .poly folder
			string relativeP = Path.GetRelativePath(projectPath, file).SanitizePath();
			if (relativeP.StartsWith(".poly/")) continue;

			if (file.EndsWith(MetaExtension))
			{
				// Store all file with meta

				string? id = ReadMetaId(file);
				if (string.IsNullOrEmpty(id)) continue;

				string targetRelative = relativeP[..^MetaExtension.Length];
				if (!File.Exists(Path.Join(projectPath, targetRelative))) continue;

				indexToFile[id] = targetRelative;
			}
			else if (file.EndsWith(".poly"))
			{
				// Store all world file
				indexToFile["world_" + relativeP] = relativeP;
			}
		}

		// List of files to process
		List<(string id, string linkedPath, string originPath)> filesToProcess = [];

		foreach ((string id, string linkedPath) in indexToFile)
		{
			string originPath = Path.GetFullPath(Path.Join(projectPath, linkedPath));
			PT.Print("Exporting ", linkedPath);

			if (!File.Exists(originPath))
			{
				PT.PrintErr(linkedPath, " doesn't exist");
				continue;
			}
			if (!PathUtils.IsPathInsideDirectory(originPath, projectPath))
			{
				PT.PrintErr(linkedPath, " is beyond project directory");
				continue;
			}

			filesToProcess.Add((id, linkedPath, originPath));
		}

		// Read all files in parallel
		ConcurrentDictionary<string, byte[]> fileContents = new();
		int totalFiles = filesToProcess.Count;
		int readCount = 0;

		ParallelOptions parallelOptions = new()
		{
			MaxDegreeOfParallelism = Math.Min(MaxParallelism, System.Environment.ProcessorCount)
		};

		await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (fileInfo, ct) =>
		{
			var (id, linkedPath, originPath) = fileInfo;

			byte[] fileData = await File.ReadAllBytesAsync(originPath, ct);
			fileContents[linkedPath] = fileData;

			int current = Interlocked.Increment(ref readCount);
			progress?.Report(new LoadOverlayProgress
			{
				Status = $"Reading {linkedPath}",
				Current = current,
				Total = totalFiles
			});
		});

		// Write to zip sequentially
		int processedCount = 0;
		foreach (var (id, linkedPath, originPath) in filesToProcess)
		{
			processedCount++;
			progress?.Report(new LoadOverlayProgress
			{
				Status = $"Packing {linkedPath}",
				Current = processedCount,
				Total = totalFiles
			});

			if (fileContents.TryGetValue(linkedPath, out byte[]? fileData))
			{
				ZipArchiveEntry entry = archive.CreateEntry(linkedPath);
				using Stream entryStream = entry.Open();
				entryStream.Write(fileData);
			}
		}

		// Pack index
		ZipArchiveEntry indexEntry = archive.CreateEntry("index.json");
		using Stream indexStream = indexEntry.Open();
		using StreamWriter indexWriter = new(indexStream);
		string indexJson = JsonSerializer.Serialize(indexToFile, ProjectJSONGenerationContext.Default.DictionaryStringString);
		indexWriter.Write(indexJson);
	}

	private static async Task PackModelToArchive(Instance model, ZipArchive archive, IProgress<LoadOverlayProgress>? progress = null)
	{
		progress?.Report(new() { Status = "Packing Model" });

		string rootFolderPath = model.Root.LinkedSession.ProjectFolderPath;
		FileLinkAsset[] fileLinks = [.. model.Root.Assets.FileLinks.Values];

		int i = 0;

		Dictionary<string, string> indexToFile = [];

		// List files to be processed
		List<(FileLinkAsset asset, string linkedPath, string originPath)> filesToProcess = [];

		foreach (FileLinkAsset f in fileLinks)
		{
			i++;
			bool hadUsed = false;
			foreach (NetworkedObject item in f.LinkedTo)
			{
				if (item == model || (item is Instance iI && iI.IsDescendantOf(model)))
				{
					hadUsed = true;
					break;
				}
			}
			if (!hadUsed) continue;

			string? linkedPath = f.LinkedPath;
			if (linkedPath == null) continue;

			indexToFile.Add(f.LinkedID, linkedPath);
			string originPath = Path.GetFullPath(Path.Join(rootFolderPath, linkedPath));

			if (!File.Exists(originPath))
			{
				PT.PrintErr(linkedPath, " doesn't exist");
				continue;
			}
			if (!PathUtils.IsPathInsideDirectory(originPath, rootFolderPath))
			{
				PT.PrintErr(linkedPath, " is beyond project directory");
				continue;
			}

			filesToProcess.Add((f, linkedPath, originPath));
		}

		ConcurrentDictionary<string, byte[]> fileContents = new();
		int totalFiles = filesToProcess.Count;
		int processedFiles = 0;

		ParallelOptions parallelOptions = new()
		{
			MaxDegreeOfParallelism = Math.Min(MaxParallelism, System.Environment.ProcessorCount)
		};

		// Parallel read
		await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (fileInfo, ct) =>
		{
			var (asset, linkedPath, originPath) = fileInfo;

			byte[] fileData = await File.ReadAllBytesAsync(originPath, ct);
			fileContents[linkedPath] = fileData;

			int current = Interlocked.Increment(ref processedFiles);
			progress?.Report(new() { Status = $"Reading {linkedPath} ({current}/{totalFiles})", Current = current, Total = totalFiles });
		});

		// Write to zip sequentially
		int writeIndex = 0;
		foreach (var (asset, linkedPath, originPath) in filesToProcess)
		{
			writeIndex++;
			progress?.Report(new() { Status = $"Writing {linkedPath} ({writeIndex}/{totalFiles})", Current = writeIndex, Total = totalFiles });


			if (fileContents.TryGetValue(linkedPath, out byte[]? fileData))
			{
				ZipArchiveEntry entry = archive.CreateEntry(linkedPath);
				using Stream entryStream = entry.Open();
				await entryStream.WriteAsync(fileData);
			}
		}

		// Pack main model data
		byte[] modelData = PolyFormat.SaveCompressedModelAsByte(model);
		ZipArchiveEntry entryFile = archive.CreateEntry("model.ptmodel");
		using (Stream entryStream = entryFile.Open())
		{
			entryStream.Write(modelData);
		}

		// Pack index
		ZipArchiveEntry indexEntry = archive.CreateEntry("index.json");
		using Stream indexStream = indexEntry.Open();
		using StreamWriter indexWriter = new(indexStream);
		string indexJson = JsonSerializer.Serialize(indexToFile, ProjectJSONGenerationContext.Default.DictionaryStringString);
		indexWriter.Write(indexJson);
	}

	public static async Task PackProjectToFile(string projectPath, string filePath, IProgress<LoadOverlayProgress>? progress = null)
	{
		byte[] data = await PackProject(projectPath, progress);
		File.WriteAllBytes(filePath, data);
	}

	public static async Task PackModelToFile(Instance model, string filePath)
	{
		byte[] data = await PackModel(model);
		File.WriteAllBytes(filePath, data);
	}

	public static async Task<byte[]> PackAddon(Script script, AddonMetadata metadata, IProgress<LoadOverlayProgress>? progress = null)
	{
		using MemoryStream stream = new();
		using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
		{
			await PackModelToArchive(script, archive, progress);

			// Add metadata
			ZipArchiveEntry metaEntry = archive.CreateEntry("addonmeta.json");
			using Stream metaStream = metaEntry.Open();
			using StreamWriter metaWriter = new(metaStream);
			string metaJson = JsonSerializer.Serialize(metadata, AddonJSONGenerationContext.Default.AddonMetadata);
			await metaWriter.WriteAsync(metaJson);
		}
		return stream.ToArray();
	}

	public static async Task PackAddonToFile(Script script, string filePath, AddonMetadata metadata)
	{
		byte[] data = await PackAddon(script, metadata);
		await File.WriteAllBytesAsync(filePath, data);
	}

	public static AddonData LoadAddonArchive(World root, byte[] data)
	{
		using MemoryStream stream = new(data);
		using ZipArchive archive = new(stream, ZipArchiveMode.Read);
		return LoadAddonFromArchive(root, archive);
	}

	public static AddonData LoadAddonFile(World root, string filePath)
	{
		using FileStream fs = File.OpenRead(filePath);
		using ZipArchive archive = new(fs, ZipArchiveMode.Read);
		return LoadAddonFromArchive(root, archive);
	}

	private static AddonData LoadAddonFromArchive(World root, ZipArchive archive)
	{
		ZipArchiveEntry metaEntry = archive.GetEntry("addonmeta.json") ?? throw new InvalidDataException("addonmeta.json file missing");
		ModelData? modelData = ReadModelDataFromArchive(archive) ?? throw new InvalidDataException("Invalid addon format");
		Instance? i = LoadPackedModelData(root, modelData.Value, root.CreatorContext, "@temp");

		// Detach model to allow reparenting
		i?.DetachModel();

		if (i is Script s)
		{
			using Stream metaStream = metaEntry.Open();
			using StreamReader metaReader = new(metaStream);
			string metaRaw = metaReader.ReadToEnd();
			AddonMetadata metadata = JsonSerializer.Deserialize(metaRaw, AddonJSONGenerationContext.Default.AddonMetadata);

			return new()
			{
				Metadata = metadata,
				EntryScript = s
			};
		}
		else
		{
			throw new InvalidDataException("Addon root is not a script");
		}
	}

	public struct AddonData
	{
		public AddonMetadata Metadata;
		public Script EntryScript;
	}
#endif

	public static CreatorProjectMetadata ReadProjectMetadata(string content)
	{
		return JsonSerializer.Deserialize(content, ProjectJSONGenerationContext.Default.CreatorProjectMetadata);
	}

	public static WorldData LoadPackedWorld(World root, byte[] data, string? entryPath = null)
	{
		using MemoryStream stream = new(data);
		using ZipArchive archive = new(stream, ZipArchiveMode.Read);
		return LoadPackedWorldFromArchive(root, archive, entryPath);
	}

	[ThreadStatic] private static bool _pacedLoad;
	[ThreadStatic] private static System.Threading.Tasks.Task? _pacedLoadTask;

	private static async System.Threading.Tasks.Task<WorldData> LoadPackedWorldFromArchiveAsync(World root, ZipArchive archive, string? entryPath = null)
	{
		_pacedLoad = true;
		WorldData data;
		System.Threading.Tasks.Task? loadTask;
		try
		{
			data = LoadPackedWorldFromArchive(root, archive, entryPath);
			loadTask = _pacedLoadTask;
		}
		finally
		{
			_pacedLoad = false;
			_pacedLoadTask = null;
		}
		if (loadTask != null)
		{
			await loadTask;
		}
		return data;
	}

	private static WorldData LoadPackedWorldFromArchive(World root, ZipArchive archive, string? entryPath = null)
	{
		CreatorProjectMetadata metadata;

		// Preload files
		Dictionary<string, byte[]> files = [];

		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (entry.FullName.EndsWith('/')) continue; // Skip directories
			using Stream entryStream = entry.Open();
			using MemoryStream ms = new();
			entryStream.CopyTo(ms);

			byte[] content = ms.ToArray();
			string relativePath = entry.FullName.SanitizePath();

			files[relativePath] = content;
		}

		root.IO.FileStructure = files;

		// Read index
		if (files.TryGetValue("index.json", out byte[]? indexBytes))
		{
			string indexDataRaw = System.Text.Encoding.UTF8.GetString(indexBytes);
			Dictionary<string, string>? indexToFile = JsonSerializer.Deserialize(indexDataRaw, ProjectJSONGenerationContext.Default.DictionaryStringString);

			if (indexToFile != null)
			{
				root.IO.IndexToFile = indexToFile;
				root.IO.FileToIndex.Clear();
				foreach (KeyValuePair<string, string> item in indexToFile)
				{
					root.IO.FileToIndex[item.Value] = item.Key;
				}
			}
		}

		// Read metadata
		if (files.TryGetValue("meta.json", out byte[]? metadataByte))
		{
			string metaDataRaw = System.Text.Encoding.UTF8.GetString(metadataByte);
			metadata = ReadProjectMetadata(metaDataRaw);
		}
		else
		{
			throw new Exception("Metadata not present");
		}

		// Load input
		if (files.TryGetValue("input.json", out byte[]? inputBytes))
		{
			string inputMapRaw = System.Text.Encoding.UTF8.GetString(inputBytes);
			root.Input.MapData = InputMapData.LoadFromString(inputMapRaw);
		}

		// Default to main
		entryPath ??= metadata.MainWorld;

		// Load world
		if (files.TryGetValue(entryPath, out byte[]? entryBytes))
		{
			if (_pacedLoad)
			{
				System.Threading.Tasks.Task t = PolyFormat.LoadWorldAsync(root, entryBytes);
				_pacedLoadTask = t;
			}
			else
			{
				PolyFormat.LoadWorld(root, entryBytes);
			}
		}

		return new()
		{
			Metadata = metadata
		};
	}

	public static void LoadPackedWorldFile(World root, string filePath, string? entryPath = null)
	{
		using FileStream fs = File.OpenRead(filePath);
		using ZipArchive archive = new(fs, ZipArchiveMode.Read);
		LoadPackedWorldFromArchive(root, archive, entryPath);
	}

	public static async System.Threading.Tasks.Task LoadPackedWorldFileAsync(World root, string filePath, string? entryPath = null)
	{
		using FileStream fs = File.OpenRead(filePath);
		using ZipArchive archive = new(fs, ZipArchiveMode.Read);
		await LoadPackedWorldFromArchiveAsync(root, archive, entryPath);
	}

	public static ModelData? ReadModelData(byte[] data)
	{
		using MemoryStream stream = new(data);
		using ZipArchive archive = new(stream, ZipArchiveMode.Read);
		return ReadModelDataFromArchive(archive);
	}

	private static ModelData? ReadModelDataFromArchive(ZipArchive archive)
	{
		ZipArchiveEntry? entryFile = archive.GetEntry("model.ptmodel");
		if (entryFile == null) return null;

		List<string> files = [.. archive.Entries
			.Where(e => !e.FullName.EndsWith('/'))
			.Select(e => e.FullName.SanitizePath())];

		using Stream entryStream = entryFile.Open();
		using MemoryStream ms = new();
		entryStream.CopyTo(ms);
		byte[] entryByte = ms.ToArray();

		PolyFormat.PolyRootData rootData = PolyFormat.ReadRootDataBytes(entryByte);

		if (rootData.Objects.Length == 0) return null;

		string packedModelName = rootData.Objects[0].Name;

		Dictionary<string, byte[]> fileContents = [];
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (entry.FullName.EndsWith('/')) continue;

			using Stream stream = entry.Open();
			using MemoryStream memStream = new();
			stream.CopyTo(memStream);
			fileContents[entry.FullName.SanitizePath()] = memStream.ToArray();
		}

		return new()
		{
			ModelName = packedModelName,
			Files = [.. files],
			RootData = rootData,
			FileContents = fileContents
		};
	}

	public static Instance? LoadPackedModel(World root, byte[] data, Instance? parent = null, string? baseAssetFolder = null)
	{
		ModelData? modelData = ReadModelData(data);

		if (modelData == null) return null;

		return LoadPackedModelData(root, modelData.Value, parent, baseAssetFolder);
	}

	public static Instance? LoadPackedModelData(World root, ModelData modelData, Instance? parent = null, string? baseAssetFolder = null)
	{
		baseAssetFolder ??= Globals.ToolboxFolderName;
		Dictionary<string, string> finalIndexToFile = [];

		if (modelData.FileContents != null)
		{
			var indexEntry = modelData.FileContents.FirstOrDefault(kvp => kvp.Key == "index.json");
			if (indexEntry.Value != null)
			{
				string indexDataRaw = System.Text.Encoding.UTF8.GetString(indexEntry.Value);
				Dictionary<string, string>? indexToFile = JsonSerializer.Deserialize(indexDataRaw, ProjectJSONGenerationContext.Default.DictionaryStringString);

				if (indexToFile != null)
				{
					foreach (KeyValuePair<string, string> item in indexToFile)
					{
						string originFilePath = item.Value;
						string writeToPath = Path.Join(baseAssetFolder, modelData.ModelName, originFilePath).SanitizePath();
						string fileID = item.Key;

						finalIndexToFile[fileID] = writeToPath;

						if (modelData.FileContents.TryGetValue(originFilePath, out byte[]? fileContent))
						{
							root.IO.WriteBytesToPath(writeToPath, fileContent);
						}
					}
				}
			}
		}

		return PolyFormat.LoadModelFromRootData(root, modelData.RootData, parent, subLoadContext: new() { IndexToFile = finalIndexToFile });
	}

	public static Instance? LoadPackedModelFile(World root, string filePath, Instance? parent = null, string? baseAssetFolder = null)
	{
		return LoadPackedModel(root, File.ReadAllBytes(filePath), parent, baseAssetFolder);
	}

	public struct ModelData
	{
		public string ModelName;
		public string[] Files;
		public PolyFormat.PolyRootData RootData;
		public Dictionary<string, byte[]>? FileContents;
	}

	public struct WorldData
	{
		public CreatorProjectMetadata Metadata;
	}
}

public struct CreatorProjectMetadata()
{
	[JsonInclude] public string ProjectName = "Project Name";
	[JsonInclude] public string MainWorld = "main.poly";
	[JsonInclude] public int? IconID;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(CreatorProjectMetadata))]
internal partial class ProjectJSONGenerationContext : JsonSerializerContext
{
}
