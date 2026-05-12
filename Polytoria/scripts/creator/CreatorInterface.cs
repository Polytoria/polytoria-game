// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator.Managers;
using Polytoria.Creator.Settings;
using Polytoria.Creator.UI;
using Polytoria.Creator.UI.Popups;
using Polytoria.Creator.UI.Splashes;
using Polytoria.Creator.UI.Wizards;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Enums;
using Polytoria.Formats;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static Polytoria.Datamodel.Creator.CreatorAddons;

namespace Polytoria.Creator;

public partial class CreatorInterface : Control, IScriptObject
{
	public const string IntroRanFile = "user://creator/introran";

	private const string CreateScriptPopupPath = "res://scenes/creator/popups/create_script.tscn";
	private const string GiveNamePopupPath = "res://scenes/creator/popups/give_name.tscn";
	private const string LinkDevicePopupPath = "res://scenes/creator/popups/link_device.tscn";
	private const string SettingsPopupPath = "res://scenes/creator/popups/settings/settings.tscn";
	private const string InputManagerPopupPath = "res://scenes/creator/popups/input_manager/input_manager.tscn";
	private const string BindKeyPopupPath = "res://scenes/creator/popups/bind_key.tscn";
	private const string PublishPopupPath = "res://scenes/creator/popups/publish/publish.tscn";
	private const string AddonReqPermPopupPath = "res://scenes/creator/popups/addon_perm_request.tscn";

	private const string CreditPopupPath = "res://scenes/creator/popups/credits.tscn";
	private const string CreatorThemePath = "res://resources/themes/creator/creator.tres";
	private string? _pendingLegacyWorld;
	private PackedScene _insertMenuPopupPacked = GD.Load<PackedScene>("res://scenes/creator/popups/insert/insert_menu.tscn");

	private Theme _creatorTheme = null!;
	private Label? _followCursorLabel;

	public StatusBar? StatusBar { get; internal set; }
	public LoadOverlay? LoadOverlay { get; internal set; }

	[ScriptProperty] public ToolModeEnum ToolMode { get; internal set; } = ToolModeEnum.Select;
	[ScriptProperty] public Color TargetPartColor { get; internal set; } = new(1, 1, 1);
	[ScriptProperty] public Part.PartMaterialEnum TargetPartMaterial { get; internal set; } = Part.PartMaterialEnum.SmoothPlastic;

	[ScriptProperty] public bool MoveSnapEnabled { get; internal set; } = true;

	[ScriptProperty]
	public float MoveSnapping
	{
		get
		{
			if (TempDisableSnap)
			{
				return MoveSnapEnabled ? 0.01f : UserMoveSnapping;
			}
			return MoveSnapEnabled ? UserMoveSnapping : 0.01f;
		}
	}

	[ScriptProperty] public float UserMoveSnapping { get; internal set; } = 1;
	[ScriptProperty] public bool RotateSnapEnabled { get; internal set; } = true;

	[ScriptProperty]
	public float RotateSnapping
	{
		get
		{
			if (TempDisableSnap)
			{
				return RotateSnapEnabled ? 0.01f : UserRotateSnapping;
			}
			return RotateSnapEnabled ? UserRotateSnapping : 0.01f;
		}
	}

	[ScriptProperty] public float UserRotateSnapping { get; internal set; } = 45;

	public static bool TempDisableSnap => Input.IsKeyPressed(Key.Alt);

	public CreatorService Service = null!;
	public Instance? PendingCreateScriptAt;

	public string LastFilePromptFolder = "";

	public InsertMenuPopup? InsertMenu { get; private set; }

	public override void _Ready()
	{
		_creatorTheme = ResourceLoader.Load<Theme>(CreatorThemePath, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		LastFilePromptFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

		Theme = _creatorTheme;

		if (!Godot.FileAccess.FileExists(IntroRanFile))
		{
			IntroductionWizard.Singleton.Open();
		}
		else
		{
			StartupSplash.Singleton.Open();
		}

		CreatorSettingsService.Instance.Changed += OnSettingChanged;
		ApplyUIScale();
		ApplyFullscreen();
		ApplyVSync();

		base._Ready();
	}

	public override void _ExitTree()
	{
		CreatorSettingsService.Instance.Changed -= OnSettingChanged;
		base._ExitTree();
	}

	private void OnSettingChanged(SettingChangedEvent e)
	{
		switch (e.Key)
		{
			case CreatorSettingKeys.Interface.UiScale:
				ApplyUIScale();
				break;
			case SharedSettingKeys.Display.Fullscreen:
				ApplyFullscreen();
				break;
			case SharedSettingKeys.Display.VSync:
				ApplyVSync();
				break;
		}
	}

	public override void _Process(double delta)
	{
		_followCursorLabel?.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(10, 10);
		base._Process(delta);
	}

	private void ApplyUIScale()
	{
		float baseUIScale = CreatorSettingsService.Instance.Get<float>(CreatorSettingKeys.Interface.UiScale);

		// Get the OS display scale factor
		int screenId = DisplayServer.WindowGetCurrentScreen();
		float osScale = DisplayServer.ScreenGetScale(screenId);

		float finalScale = baseUIScale * osScale;
		PT.Print($"Final UI Scale: {finalScale}");
		GetWindow().ContentScaleFactor = finalScale;
	}

	private static void ApplyFullscreen()
	{
		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			return;
		}

		DisplayServer.WindowSetMode(CreatorSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.Fullscreen) ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized);
	}

	private static void ApplyVSync()
	{
		bool useVSync = CreatorSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.VSync);
		DisplayServer.WindowSetVsyncMode(useVSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		OS.LowProcessorUsageMode = useVSync;
	}

	public static void CreateNewWorld()
	{
		NewProjectWizard.Singleton.Open();
	}

	public async void OpenWorldFile(string filePath)
	{
		string fileName = filePath.GetFile();

		if (fileName == Globals.ProjectMetaFileName)
		{
			await CreatorService.Singleton.CreateNewSession(filePath);
		}
		else
		{
			PolyFileTypeEnum fileType = await DatamodelLoader.DetermineFileType(filePath);

			if (fileType == PolyFileTypeEnum.PolyXML)
			{
				bool yes = await PromptConfirmation("This file is in a legacy format. To edit it, you must convert it to a Polytoria project. Convert now?");

				if (yes)
				{
					_pendingLegacyWorld = filePath;
					PromptFileSelect(new()
					{
						Title = "Choose Destination",
						CurrentDirectory = filePath.GetBaseDir(),
						DialogMode = DisplayServer.FileDialogMode.OpenDir
					}, OnOpenConversion);
				}
			}
			else if (fileType == PolyFileTypeEnum.Packed)
			{
				await CreatorService.Singleton.CreateNewSession(filePath);
			}
			else
			{
				PopupAlert("Unknown file format");
			}
		}
	}

	private async void OnOpenConversion(string[] paths)
	{
		string path = paths[0];

		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		if (_pendingLegacyWorld == null) return;

		string fName = _pendingLegacyWorld.GetFile().TrimExtension();
		string createAt = Path.GetFullPath(Path.Join(path, fName));

		if (!Directory.Exists(createAt))
		{
			Directory.CreateDirectory(createAt);
		}

		try
		{
			await ProjectManager.ImportLegacyWorld(_pendingLegacyWorld,
			createAt,
			new()
			{
				ProjectName = fName.Capitalize(),
				MainWorld = "main.poly"
			});
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			PopupAlert(ex.Message);
		}
	}

	public void PromptImportModel()
	{
		if (World.Current == null) return;

		PromptFileSelect(new()
		{
			Title = "Import model",
			DialogMode = DisplayServer.FileDialogMode.OpenFile,
			Filters = ["*.ptmd;Polytoria Model"]
		}, OnPromptImportModelFile);
	}

	private async void OnPromptImportModelFile(string[] paths)
	{
		ImportModel(paths[0]);
	}

	public async void ImportModel(string modelImportPath)
	{
		if (World.Current == null) return;
		if (CreatorService.CurrentSession == null) return;

		CreatorSession session = CreatorService.CurrentSession;

		try
		{
			Instance? i = await DatamodelLoader.LoadModelFile(World.Current, modelImportPath, World.Current.Environment);
			if (i != null)
			{
				World.Current.CreatorContext.Selections.SelectOnly(i);
				session.RescanFolder();
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			PopupAlert(ex.Message, "Error importing model");
		}
	}

	public void ExportSelectedModel()
	{
		if (World.Current == null) return;

		List<Instance> instances = World.Current.CreatorContext.Selections.SelectedInstances;
		if (instances.Count == 0)
		{
			PopupAlert("Please select any instance before exporting");
			return;
		}

		Instance target = instances[0];

		if (target.GetType().IsDefined(typeof(StaticAttribute), true))
		{
			PopupAlert("Cannot export a static class");
			return;
		}

		PromptFileSelect(new()
		{
			Title = "Export model",
			FileName = $"{target.Name}.ptmd",
			DialogMode = DisplayServer.FileDialogMode.SaveFile,
			Filters = ["*.ptmd;Polytoria Model"]
		}, async (string[] paths) =>
		{
			if (paths.Length > 0)
			{
				string path = paths[0];
				if (!path.EndsWith(".ptmd"))
				{
					path += ".ptmd";
				}

				try
				{
					await PackedFormat.PackModelToFile(target, path);
					CreatorService.Interface.StatusBar?.SetStatus("Exported model to " + path);
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					PopupAlert(ex.Message, "Error exporting model");
				}
			}
		});
	}
	public void PromptOpenWorld()
	{
		PromptFileSelect(new FileSelectPromptPayload()
		{
			Title = "Open World",
			DialogMode = DisplayServer.FileDialogMode.OpenFile,
			Filters = ["*.ptproj,*.poly;Polytoria World", "*.ptm,*.spm;Legacy World"]
		}, OnWorldOpen);
	}

	private void OnWorldOpen(string[] paths)
	{
		string path = paths[0];

		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		OpenWorldFile(path);
	}

	public void PromptCreateScript(Instance? atInstance = null, string? atPath = null)
	{
		CreateScriptPopup popup = Globals.CreateInstanceFromScene<CreateScriptPopup>(CreateScriptPopupPath);
		PendingCreateScriptAt = atInstance;
		popup.CreateAt = atPath;
		PopupWindow(popup);
	}

	public void PromptCreateFolder(string atPath)
	{
		if (CreatorService.CurrentSession == null) return;
		CreatorSession session = CreatorService.CurrentSession;
		PromptGiveName("Folder name...", async name =>
		{
			try
			{
				string createAt = Path.Join(atPath, name).SanitizePath();
				await session.CreateFolder(createAt);
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				PopupAlert(ex.Message, "Error creating folder");
			}
		}, "Give Folder name");
	}


	public void PromptCreateWorld(string atPath)
	{
		if (CreatorService.CurrentSession == null) return;
		CreatorSession session = CreatorService.CurrentSession;
		PromptGiveName("World file name...", async name =>
		{
			try
			{
				string createAt = Path.Join(atPath, name).SanitizePath();

				if (!createAt.EndsWith(".poly"))
				{
					createAt += ".poly";
				}

				await session.CreateWorld(createAt);
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				PopupAlert(ex.Message, "Error creating world");
			}
		}, "Give World name");
	}

	public async void PromptDeleteFiles(string[] files)
	{
		if (files.Length == 0) return;
		if (CreatorService.CurrentSession == null) return;
		CreatorSession session = CreatorService.CurrentSession;
		string wordToUse = $"these {files.Length} files/folders";

		if (files.Length == 1)
		{
			wordToUse = files[0].GetFile();

			if (wordToUse == "")
			{
				wordToUse = "this folder";
			}
		}

		if (!await PromptConfirmation("Are you sure you want to delete " + wordToUse + "? You can recover this from the recycle bin")) return;
		try
		{
			foreach (string item in files)
			{
				session.RemoveFile(item, toRecycleBin: true);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			PopupAlert(ex.Message, "Error deleting files");
		}
		session.RescanFolder();
	}

	public async Task<bool> PromptConfirmation(string msg, string title = "Please Confirm", string? dismissKey = null)
	{
		if (dismissKey != null)
		{
			bool isShown = CreatorSettingsService.Instance.Get<bool>(dismissKey);
			if (!isShown) return true;
		}

		TaskCompletionSource<bool> tcs = new();

		ConfirmationDialog dialog = new()
		{
			WrapControls = true,
			Title = title,
			DialogCloseOnEscape = true,
		};

		VBoxContainer v = new() { Alignment = BoxContainer.AlignmentMode.Center };
		dialog.AddChild(v);

		Label txt = new() { Text = msg, HorizontalAlignment = HorizontalAlignment.Center };
		v.AddChild(txt);

		dialog.Confirmed += () =>
		{
			tcs.SetResult(true);
			if (dismissKey != null)
			{
				CreatorSettingsService.Instance.Set(dismissKey, false);
			}
			dialog.QueueFree();
		};

		dialog.Canceled += () =>
		{
			tcs.SetResult(false);
			dialog.QueueFree();
		};

		if (dismissKey != null)
		{
			CheckBox chk = new() { Text = "Don't show this again.", SizeFlagsHorizontal = SizeFlags.ShrinkCenter | SizeFlags.Expand };
			v.AddChild(chk);
		}

		PopupWindow(dialog);

		return await tcs.Task;
	}

	public void PromptRenameFile(string filePath)
	{
		if (CreatorService.CurrentSession == null) return;
		PromptGiveName("File name...", s =>
		{
			CreatorService.CurrentSession.RenameFile(filePath, s);
		}, "Rename file", filePath.GetFile());
	}

	public void OpenLinkDevicePrompt()
	{
		LinkDevicePopup popup = Globals.CreateInstanceFromScene<LinkDevicePopup>(LinkDevicePopupPath);
		PopupWindow(popup);
	}

	public void OpenSettings()
	{
		SettingsPopup popup = Globals.CreateInstanceFromScene<SettingsPopup>(SettingsPopupPath);
		PopupWindow(popup);
	}

	public void OpenPublish(Instance target)
	{
		PublishPopup popup = Globals.CreateInstanceFromScene<PublishPopup>(PublishPopupPath);
		popup.Target = target;
		PopupWindow(popup);
	}

	public void OpenInputManager()
	{
		InputManagerPopup popup = Globals.CreateInstanceFromScene<InputManagerPopup>(InputManagerPopupPath);
		PopupWindow(popup);
	}

	public async Task<bool> PromptAddonReqPerm(AddonPermissionEnum[] perms, PackedFormat.AddonData data)
	{
		TaskCompletionSource<bool> tcs = new();
		AddonPermRequestPopup popup = Globals.CreateInstanceFromScene<AddonPermRequestPopup>(AddonReqPermPopupPath);
		popup.RequestedPerms = perms;
		popup.AddonData = data;
		PopupWindow(popup);

		void onRes(bool val)
		{
			popup.Responded -= onRes;
			tcs.SetResult(val);
		}

		popup.Responded += onRes;

		return await tcs.Task;
	}

	public void PromptBindKey(Action<KeyCodeEnum> callback)
	{
		BindKeyPopup popup = Globals.CreateInstanceFromScene<BindKeyPopup>(BindKeyPopupPath);
		popup.KeyBinded += callback.Invoke;
		PopupWindow(popup);
	}

	public void PopupCredits()
	{
		CreditsPopup popup = Globals.CreateInstanceFromScene<CreditsPopup>(CreditPopupPath);
		PopupWindow(popup);
	}

	public void PopupManageAddons()
	{
		OS.ShellShowInFileManager(ProjectSettings.GlobalizePath(AddonsManager.UserAddonFolder));
	}

	public void PromptGiveName(string placeholder, Action<string> callback, string title = "Name...", string defaultValue = "")
	{
		GiveNamePopup popup = Globals.CreateInstanceFromScene<GiveNamePopup>(GiveNamePopupPath);
		popup.Placeholder = placeholder;
		popup.DefaultValue = defaultValue;
		popup.Title = title;
		popup.Submitted += callback;
		PopupWindow(popup);
	}

	public void PopupAlert(string msg, string title = "Notice")
	{
		AcceptDialog dialog = new()
		{
			DialogText = msg,
			Title = title,
		};

		PopupWindow(dialog);
	}

	public void PopupWindow(Window window)
	{
		window.Visible = false;
		window.ForceNative = true;
		window.Theme = _creatorTheme;
		AddChild(window);
		window.PopupCentered();
	}

	public InsertMenuPopup OpenInsertMenu(Instance? insertTo = null)
	{
		if (IsInstanceValid(InsertMenu))
		{
			InsertMenu?.QueueFree();
		}
		InsertMenu = _insertMenuPopupPacked.Instantiate<InsertMenuPopup>();
		InsertMenu.InsertTo = insertTo;
		AddChild(InsertMenu);
		InsertMenu.PopupAtCursor();
		InsertMenu.GrabFocus();
		return InsertMenu;
	}

	public void PromptFileSelect(FileSelectPromptPayload data, Action<string[]> callback, Action? onCancel = null)
	{
		bool replaceCur = string.IsNullOrEmpty(data.CurrentDirectory);
		string currentDir = replaceCur ? LastFilePromptFolder : data.CurrentDirectory;

		FileDialog dialog = new()
		{
			Title = data.Title,
			CurrentDir = currentDir,
			CurrentFile = data.FileName,
			ShowHiddenFiles = data.ShowHidden,
			FileMode = MapFileMode(data.DialogMode),
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
		};

		if (data.Filters is { Length: > 0 })
			dialog.Filters = data.Filters;

		AddChild(dialog);

		void OnPathsSelected(string[] paths)
		{
			if (replaceCur)
				LastFilePromptFolder = paths[0].GetBaseDir();
			callback.Invoke(paths);
			dialog.QueueFree();
		}

		dialog.FileSelected += path => OnPathsSelected([path]);
		dialog.DirSelected += path => OnPathsSelected([path]);
		dialog.FilesSelected += paths => OnPathsSelected(paths);
		dialog.Canceled += () => { onCancel?.Invoke(); dialog.QueueFree(); };

		dialog.PopupCentered(new Vector2I(800, 600));
	}

	private static FileDialog.FileModeEnum MapFileMode(DisplayServer.FileDialogMode mode) => mode switch
	{
		DisplayServer.FileDialogMode.OpenFile => FileDialog.FileModeEnum.OpenFile,
		DisplayServer.FileDialogMode.OpenFiles => FileDialog.FileModeEnum.OpenFiles,
		DisplayServer.FileDialogMode.OpenDir => FileDialog.FileModeEnum.OpenDir,
		DisplayServer.FileDialogMode.SaveFile => FileDialog.FileModeEnum.SaveFile,
		_ => FileDialog.FileModeEnum.OpenFile,
	};

	public static void ToggleFullscreen()
	{
		var settings = CreatorSettingsService.Instance;
		settings.Set(SharedSettingKeys.Display.Fullscreen, !settings.Get<bool>(SharedSettingKeys.Display.Fullscreen));
	}

	public void StartFollowCursorLabel(string text)
	{
		StopFollowCursorLabel();
		_followCursorLabel = new() { Text = text };
		AddChild(_followCursorLabel);
	}

	public void StopFollowCursorLabel()
	{
		if (_followCursorLabel != null && IsInstanceValid(_followCursorLabel))
		{
			_followCursorLabel.QueueFree();
		}
		_followCursorLabel = null;
	}

	/// <summary>
	/// Request confirmation before quit
	/// </summary>
	/// <returns>Boolean that indicates if this app should quit</returns>
	internal async Task<bool> OnQuitRequested()
	{
		return await PromptConfirmation("Are you sure you want to quit? Any unsaved changes will be lost");
	}
}

public struct FileSelectPromptPayload()
{
	public string Title = "Select file...";
	public string CurrentDirectory = "";
	public string FileName = "";
	public bool ShowHidden = false;
	public DisplayServer.FileDialogMode DialogMode;
	public string[] Filters = [];
}
