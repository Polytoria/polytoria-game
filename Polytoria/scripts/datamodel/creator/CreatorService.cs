// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator;
using Polytoria.Creator.Debugger;
using Polytoria.Creator.Settings;
using Polytoria.Creator.Managers;
using Polytoria.Creator.UI;
using Polytoria.Creator.UI.Splashes;
using Polytoria.Creator.Utils;
using Polytoria.Formats;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Creator;

[Static("Creator"), ExplorerExclude]
public sealed partial class CreatorService : Node, IScriptObject
{
	public const string PolytoriaFolderName = "Polytoria/";

	private long _localTestIDCounter = 0;

	[ScriptProperty] public static CreatorInterface Interface { get; private set; } = null!;
	public static CreatorClipboard Clipboard { get; private set; } = null!;

	[ScriptProperty] public static World? CurrentGame => World.Current;

	public static CreatorService Singleton { get; set; } = null!;
	public static CreatorSession? CurrentSession { get; internal set; }

	[ScriptProperty] public PTSignal LocalTestStarted { get; private set; } = new();
	[ScriptProperty] public PTSignal LocalTestStopped { get; private set; } = new();
	[ScriptProperty] public bool LocalTestActive => LocalTestProcesses.Count != 0;
	public List<int> LocalTestProcesses { get; private set; } = [];
	public List<string> LocalTestWorlds { get; private set; } = [];
	public int LocalTestPlayerCount { get; set; } = 1;
	public static List<CreatorSession> Sessions { get; private set; } = [];
	public static Dictionary<string, CreatorSession> LocalTestIDToSession { get; private set; } = [];
	public static Dictionary<CreatorSession, string> SessionToLocalTestID { get; private set; } = [];

	internal DebugServer DebugServer { get; private set; } = null!;

	public CreatorService()
	{
		Singleton = this;
		Interface = new()
		{
			Service = this
		};
		Clipboard = new()
		{
			Service = this
		};
		AddChild(Interface);

		string polyFolder = Path.Join(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), PolytoriaFolderName);
		if (!Directory.Exists(polyFolder))
		{
			Directory.CreateDirectory(polyFolder);
		}
	}

	public override void _Ready()
	{
		OS.LowProcessorUsageMode = true;
		Globals.BeforeQuit += OnBeforeQuit;
		DebugServer = new();
		DebugServer.Start();

		DisplayServer.WindowSetDropFilesCallback(Callable.From<string[]>(OnFilesDropped));
		base._Ready();
	}

	public override void _ExitTree()
	{
		Globals.BeforeQuit -= OnBeforeQuit;
		base._ExitTree();
	}

	private void OnBeforeQuit()
	{
		try
		{
			StopLocalTest();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Error while quitting: ", ex);
		}
		try
		{
			CleanupSessions();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Error while quitting: ", ex);
		}
	}

	private async void OnFilesDropped(string[] files)
	{
		string firstFile = files[0];
		string firstFileExt = firstFile.GetExtension();

		if (firstFileExt == "ptmd")
		{
			Interface.ImportModel(firstFile);
		}
		else if (firstFileExt == "ptproj")
		{
			await CreateNewSession(firstFile);
		}
		else if (firstFileExt == "poly")
		{
			Interface.OpenWorldFile(firstFile);
		}
	}

	public override void _Process(double delta)
	{
		if (LocalTestActive)
		{
			foreach (int procID in LocalTestProcesses.ToArray())
			{
				if (!OS.IsProcessRunning(procID))
				{
					LocalTestProcesses.Remove(procID);
				}
			}

			if (LocalTestProcesses.Count == 0)
			{
				CleanupLocalTest();
				LocalTestStopped.Invoke();
			}
		}
		base._Process(delta);
	}

	public async Task CreateNewSession(string projectFilePath = "", World? worldOverride = null)
	{
		string? targetPlace = null;
		projectFilePath = ProjectSettings.GlobalizePath(projectFilePath);

		if (File.GetAttributes(projectFilePath) == FileAttributes.Directory || projectFilePath.GetExtension() == "poly")
		{
			string originFilePath = projectFilePath;
			if (projectFilePath.GetExtension() == "poly")
			{
				projectFilePath += "/../";
			}
			string projectFileRoot = Path.GetFullPath(Path.Join(projectFilePath, Globals.ProjectMetaFileName));
			if (!File.Exists(projectFileRoot))
			{
				Interface.PopupAlert("Couldn't find the project file");
				return;
			}

			if (originFilePath.GetExtension() == "poly")
			{
				targetPlace = originFilePath;
			}

			projectFilePath = projectFileRoot;
		}

		projectFilePath = projectFilePath.SanitizePath();

		PT.Print("Opening ", projectFilePath);

		string folder = Path.GetFullPath(Path.Combine(projectFilePath, "../")).SanitizePath();
		CreatorSession session = new()
		{
			ProjectFolderPath = folder,
			ProjectFilePath = projectFilePath
		};
		AddChild(session);

		Interface.StatusBar?.SetStatus("Initializing...");

		Interface.LoadOverlay?.SetTitle("Opening project");
		Interface.LoadOverlay?.SetStatus("Initializing");
		Interface.LoadOverlay?.SetMaxProgress(2);
		Interface.LoadOverlay?.Show();

		try
		{
			await session.Init();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			Interface.PopupAlert(ex.Message, "Error opening project");
			throw;
		}

		Interface.StatusBar?.SetStatus("Opening world...");
		Interface.LoadOverlay?.SetStatus("Opening world");
		Interface.LoadOverlay?.SetProgress(1);

		// Open world
		if (targetPlace != null)
		{
			session.OpenWorld(Path.GetRelativePath(folder, targetPlace).SanitizePath(), worldOverride);
		}
		else
		{
			session.OpenMainWorld(worldOverride);
		}

		Interface.LoadOverlay?.Hide();

		Sessions.Add(session);

		// Add to recents
		await ProjectManager.AddToRecents(folder);

		// Close startup splash on open file
		StartupSplash.Singleton.Close();
		Interface.StatusBar?.SetEmpty();
	}

	public static void SaveCurrentFile()
	{
		if (World.Current == null) { CreatorService.Interface.StatusBar?.SetStatus("No current game opened, did not save"); return; }
		if (CurrentSession == null) { CreatorService.Interface.StatusBar?.SetStatus("No session, did not save"); return; }
		string placePath = CurrentSession.GlobalizePath(World.Current.WorldFilePath!);

		Interface.LoadOverlay?.SetTitle("Saving world");
		Interface.LoadOverlay?.SetStatus("Saving world");
		Interface.LoadOverlay?.SetMaxProgress(2);
		Interface.LoadOverlay?.Show();

		try
		{
			PolyFormat.SaveWorldToFile(World.Current, placePath);
		}
		catch (Exception ex)
		{
			Interface.PopupAlert(ex.Message, "Error saving file");
			throw;
		}

		Interface.LoadOverlay?.SetStatus("Saving index...");
		Interface.LoadOverlay?.SetProgress(1);

		CurrentSession.Save();
		Interface.StatusBar?.SetStatus("Saved to " + placePath + " at " + DateTime.Now.ToString("HH:mm:ss"));
		Interface.LoadOverlay?.Hide();
	}

	public static void SaveCurrentFileAs()
	{
		if (World.Current == null) { Interface.StatusBar?.SetStatus("No current game opened, did not save"); return; }
		if (CurrentSession == null) { Interface.StatusBar?.SetStatus("No session, did not save"); return; }

		Interface.PromptFileSelect(new()
		{
			Title = "Save as...",
			CurrentDirectory = CurrentSession.ProjectFolderPath,
			Filters = ["*.poly;Polytoria World"],
			DialogMode = DisplayServer.FileDialogMode.SaveFile,
		}, async paths =>
		{
			try
			{
				string path = paths[0];

				if (!path.EndsWith(".poly"))
				{
					path += ".poly";
				}

				if (!PathUtils.IsPathInsideDirectory(path, CurrentSession.ProjectFolderPath))
				{
					Interface.PopupAlert("World file cannot be saved outside of project directory.");
					return;
				}

				PolyFormat.SaveWorldToFile(World.Current, path);
				CurrentSession.RescanFolder();
			}
			catch (Exception ex)
			{
				Interface.PopupAlert(ex.Message, "Error saving file");
				throw;
			}
		});
	}

	public static async void PackCurrentProject()
	{
		if (World.Current == null) { PT.Print("No current game opened, did not save"); return; }
		string? exportPath = ProjectSettings.GlobalizePath("res://test.ptpacked");

		await PackedFormat.PackProjectToFile(World.Current.LinkedSession.ProjectFolderPath, exportPath);
		Interface.StatusBar?.SetStatus("Packed to " + exportPath);
	}

	public static void Redo()
	{
		CurrentGame?.CreatorContext.History.Redo();
	}

	public static void Undo()
	{
		CurrentGame?.CreatorContext.History.Undo();
	}

	public void OpenScript(Script script)
	{
		if (CurrentSession == null) return;
		if (script.LinkedScript != null)
		{
			string? scriptPath = script.LinkedScript.LinkedPath;
			if (scriptPath == null)
			{
				// TODO: We should have a popup dialog showing invalid references
				Interface.PopupAlert("Script's file reference's invalid, please reinsert the script from the file browser.");
				return;
			}
			PT.Print("Opening ", scriptPath);
			OpenFile(scriptPath);
		}
		else
		{
			Interface.PopupAlert("Script does not have file reference");
		}
	}

	public static async void OpenFile(string path)
	{
		if (CurrentSession == null) return;
		string pathRelative = path;
		path = CurrentSession.GlobalizePath(path);

		string ext = pathRelative.GetExtension();

		if (ext == "poly")
		{
			CurrentSession.OpenWorld(pathRelative);
			return;
		}
		else if (ext == "model")
		{
			if (World.Current == null) return;
			await CurrentSession.InsertModel(pathRelative, World.Current.Environment);
			return;
		}
		else if (pathRelative == Globals.ProjectInputMapName)
		{
			Interface.OpenInputManager();
			return;
		}

		PreferredEditorEnum userPref = CreatorSettingsService.Instance.Get<PreferredEditorEnum>(CreatorSettingKeys.CodeEditor.PreferredEditor);

		if (Globals.ScriptFileExtensions.Contains(path.GetExtension()))
		{
			if (userPref == PreferredEditorEnum.BuiltIn)
			{
				Tabs.Singleton.Insert(new Tabs.TextEditorTab() { Session = CurrentSession, TargetPath = pathRelative, Title = pathRelative.GetFile() });
				return;
			}
		}

		if (userPref == PreferredEditorEnum.VSCode)
		{
			CurrentSession.CreateVSCodeConfig();
			// open in vscode
			System.Diagnostics.Process p = new();
			p.StartInfo.FileName = "code";
			p.StartInfo.Arguments = $"\"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			p.StartInfo.UseShellExecute = true;
			p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			p.Start();
			return;
		}
		else if (userPref == PreferredEditorEnum.Zed)
		{
			// open in zed
			System.Diagnostics.Process p = new();
			p.StartInfo.FileName = "zed";
			p.StartInfo.Arguments = $"\"{path}\" \"{CurrentSession.ProjectFolderPath}\"";
			p.StartInfo.UseShellExecute = true;
			p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			p.Start();
			return;
		}

		OS.ShellOpen(path);
	}

	public override async void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_copy"))
		{
			await Clipboard.SetClipboardToSelected();
		}
		else if (@event.IsActionPressed("ui_paste_into"))
		{
			await Clipboard.PasteClipboard(true);
		}
		else if (@event.IsActionPressed("ui_paste"))
		{
			await Clipboard.PasteClipboard();
		}
		else if (@event.IsActionPressed("stop_playtest"))
		{
			StopLocalTest();
		}
		base._UnhandledKeyInput(@event);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusIn)
		{
			CurrentSession?.RescanFolder();
		}
	}

	public static ScriptTypeEnum GetScriptTypeFromPath(string filePath)
	{
		string fileName = filePath.GetFile();
		string fileExt = filePath.GetExtension();

		if (!Globals.ScriptFileExtensions.Contains(fileExt))
		{
			return ScriptTypeEnum.Unknown;
		}

		string baseName = fileName.GetBaseName();

		string[] parts = baseName.Split(".");

		string scriptType = "";

		if (parts.Length >= 2)
		{
			scriptType = parts[^1];
		}
		else if (parts.Length == 1)
		{
			scriptType = "";
		}

		return scriptType switch
		{
			"server" => ScriptTypeEnum.Server,
			"client" => ScriptTypeEnum.Client,
			_ => ScriptTypeEnum.Module
		};
	}

	public static string GetScriptNameFromPath(string filePath)
	{
		string fileName = filePath.GetFile();

		// Remove .luau extension
		string baseName = fileName.Replace(".luau", "");

		// Split by dots
		string[] parts = baseName.Split(".");

		string scriptName = "";

		if (parts.Length >= 2)
		{
			scriptName = parts[0];
		}
		else if (parts.Length == 1)
		{
			scriptName = parts[0];
		}

		return scriptName;
	}

	public async void StartLocalTest(bool atCamera = false)
	{
		if (World.Current == null) { PT.PrintErr("World is null, did not test"); return; }
		World game = World.Current;
		CreatorSession session = game.LinkedSession;

		// Check if current session is already open
		if (SessionToLocalTestID.ContainsKey(session)) return;

		// Save current project
		SaveCurrentFile();

		string debugID = _localTestIDCounter++.ToString();

		LocalTestIDToSession.Add(debugID, session);
		SessionToLocalTestID.Add(session, debugID);
		await StartLocalTestOnEntry(session.ProjectFolderPath, game.WorldFilePath!, debugID, GD.RandRange(20000, 30000), false, atCamera ? game.CreatorContext.Freelook.Position : null);

		DebugConsole.Singleton.Clear();
		LocalTestStarted.Invoke();
	}

	public async Task StartLocalTestOnEntry(string projectPath, string entryPath, string debugID, int port, bool isSubplace, Vector3? spawnPos = null)
	{
		string tempPath = Path.GetTempPath();
		string placeFilePath = tempPath.PathJoin("pt_test_" + new DateTimeOffset(DateTime.Now).Millisecond + ".zip");

		await PackedFormat.PackProjectToFile(projectPath, placeFilePath, Interface.LoadOverlay.CreateProgressReporter("Starting local test..."));
		Interface.LoadOverlay?.Hide();
		StartLocalTestServer(placeFilePath, entryPath, debugID, port, isSubplace, spawnPos);
	}

	private void StartLocalTestServer(string placeFilePath, string entryPath, string debugID, int port, bool isSubplace = false, Vector3? spawnPos = null)
	{
		string exePath = OS.GetExecutablePath();

		List<string> args = ["--log-file", "user://logs/server.log", "-solo", placeFilePath, "-entry", entryPath, "-debug", $"127.0.0.1:{DebugServer.Port}", "-debug-id", debugID, "-port", port.ToString()];

		if (spawnPos != null)
		{
			args.AddRange(["-spawnpos", $"v{(int)spawnPos.Value.X},{(int)spawnPos.Value.Y},{(int)spawnPos.Value.Z}"]);
		}

		if (isSubplace)
		{
			args.AddRange(["-subworld"]);
		}
		else
		{
			args.AddRange(["-nplr", LocalTestPlayerCount.ToString()]);
		}

		if (!OS.HasFeature("serverpov"))
		{
			args.InsertRange(0, ["--headless"]);
		}

		if (Globals.IsInGDEditor)
		{
			args.InsertRange(0, ["--remote-debug", "tcp://127.0.0.1:6007"]);
		}

		// Apply Creator token for loading unapproved assets
		if (PolyCreatorAPI.Token != string.Empty)
		{
			args.InsertRange(0, ["-ctoken", PolyCreatorAPI.Token]);
		}

		args.AddRange("--rendering-method", RenderingDeviceSwitcher.GetCurrentDriverName());

		// Ignore rendering method switcher flag, use the same one as creator's
		args.Add("-rmswignore");

		LocalTestWorlds.Add(placeFilePath);

		int procID = OS.CreateProcess(exePath, [.. args]);
		PT.Print("Starting server with args: ", string.Join(" ", args));

		LocalTestProcesses.Add(procID);
	}

	public async void StopLocalTest()
	{
		if (!LocalTestActive) return;
		foreach (int item in LocalTestProcesses)
		{
			OS.Kill(item);
		}
		DebugServer.SendTerminateProgram();
	}

	public static void MigrateCoordinates(World root)
	{
		string worldFilePath = root.WorldFilePath!;
		root.ForceDelete();
		root.LinkedSession.OpenWorld(worldFilePath, migrateCoords: true);
	}

	private void CleanupLocalTest()
	{
		foreach (string item in LocalTestWorlds)
		{
			try
			{
				File.Delete(item);
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}
		LocalTestWorlds.Clear();
		LocalTestIDToSession.Clear();
		SessionToLocalTestID.Clear();
	}

	private static void CleanupSessions()
	{
		foreach (var session in Sessions)
		{
			session.Dispose();
		}
	}
}

public enum ToolModeEnum
{
	Select,
	Move,
	Rotate,
	Scale,
	Paint,
	Brush
}

public enum ScriptTypeEnum
{
	Server,
	Client,
	Module,
	Unknown
}
