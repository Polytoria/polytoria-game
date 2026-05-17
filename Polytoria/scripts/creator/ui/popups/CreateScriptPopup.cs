// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class CreateScriptPopup : PopupWindowBase
{
	[Export] private ButtonGroup _scriptGroup = null!;
	[Export] private LineEdit _pathEdit = null!;
	[Export] private Button _browseBtn = null!;
	[Export] private Button _createBtn = null!;
	[Export] private Button _cancelBtn = null!;
	[Export] private Label _errorLabel = null!;

	public string? CreateAt = "";

	private string _scriptPath = "scripts/script.server.luau";

	public override void _Ready()
	{
		CreateAt ??= "scripts/server/";
		_scriptPath = CreateAt + "script.server.luau";
		base._Ready();
		_errorLabel.Text = "";
		_pathEdit.Text = _scriptPath;
		_pathEdit.Select(CreateAt.Length, _scriptPath.Length - 12);
		_pathEdit.CaretColumn = _scriptPath.Length - 12;
		_pathEdit.GrabFocus();

		_pathEdit.GuiInput += @event =>
		{
			if (@event.IsActionPressed("ui_accept"))
			{
				SubmitCreateScript();
			}
		};

		_scriptGroup.Pressed += btn =>
		{
			string baseDir = _pathEdit.Text.GetBaseDir();

			string typeFolder = "";
			if ((baseDir.EndsWith("/server") ||
				 baseDir.EndsWith("/client") ||
				 baseDir.EndsWith("/modules")) &&
				baseDir.GetBaseDir() == "scripts")
			{
				// Removing the script type folder, since we're appending a new one
				baseDir = baseDir.GetBaseDir();
				typeFolder = btn.Name;
				if (typeFolder == "module")
				{
					// The filesystem folder for module scripts has a 's' in the end
					typeFolder = "modules";
				}
				typeFolder += '/';
			}

			string scriptName = CreatorService.GetScriptNameFromPath(_pathEdit.Text);
			ScriptTypeEnum scriptType = btn.Name.ToString() switch
			{
				"server" => ScriptTypeEnum.Server,
				"client" => ScriptTypeEnum.Client,
				_ => ScriptTypeEnum.Module,
			};

			string scriptTypeExtension = $".{btn.Name}";

			if (scriptType == ScriptTypeEnum.Module)
			{
				scriptTypeExtension = "";
			}

			string pathPrefix = string.IsNullOrEmpty(baseDir) ? "" : baseDir + '/';

			_pathEdit.Text = $"{pathPrefix}{typeFolder}{scriptName}{scriptTypeExtension}.luau";
		};

		_browseBtn.Pressed += () =>
		{
			CreatorService.Interface.PromptFileSelect(new()
			{
				Title = "Choose script path",
				FileName = _pathEdit.Text.GetFile(),
				DialogMode = DisplayServer.FileDialogMode.SaveFile,
				CurrentDirectory = CreatorService.CurrentSession!.GlobalizePath(_pathEdit.Text.GetBaseDir())
			}, paths =>
			{
				try
				{
					if (paths.Length == 0) return;

					string path = CreatorService.CurrentSession!.LocalizePath(paths[0]);

					if (!path.EndsWith(".luau"))
					{
						path += ".luau";
					}

					_pathEdit.Text = path;
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					CreatorService.Interface.PopupAlert(ex.Message);
				}
			});
		};

		_createBtn.Pressed += SubmitCreateScript;

		_cancelBtn.Pressed += () =>
		{
			CreatorService.Interface.PendingCreateScriptAt = null;
			QueueFree();
		};
	}

	private void SetErrorMessage(string msg)
	{
		_errorLabel.Text = msg;
	}

	private void SubmitCreateScript()
	{
		_scriptPath = _pathEdit.Text;
		string baseName = _scriptPath.GetBaseName();
		if (string.IsNullOrWhiteSpace(baseName))
		{
			SetErrorMessage("Give your script a name!");
			return;
		}
		if (!_scriptPath.EndsWith(".luau"))
		{
			SetErrorMessage("Script file name must ends with .luau");
			return;
		}

		if (CreatorService.CurrentSession!.FileExists(_scriptPath))
		{
			SetErrorMessage(_scriptPath.GetFile() + " already exists");
			return;
		}

		try
		{
			CreatorService.CurrentSession!.CreateScript(_scriptPath);
			QueueFree();
		}
		catch (Exception ex)
		{
			OS.Alert(ex.Message);
		}
	}
}
