// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Managers;
using Polytoria.Creator.UI.Splashes;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.IO;

namespace Polytoria.Creator.UI.Wizards;

public partial class NewProjectWizard : Control
{
	public const string TemplatesCardPath = "res://scenes/creator/wizard/new_project/component/template_place_card.tscn";
	public const string DefaultProjectName = "MyProject";
	public bool ReturnToSplash = false;

	[Export] private LineEdit _projectNameEdit = null!;
	[Export] private LineEdit _projectPathEdit = null!;
	[Export] private Button _pathBrowseBtn = null!;
	[Export] private Button _createBtn = null!;
	[Export] private Button _backBtn = null!;
	[Export] private Control _templatesContainer = null!;
	[Export] private BaseButton _gitCheckBtn = null!;
	private ButtonGroup _templateBtnGroup = new();
	private string _targetTemplatePath = "";
	private string _oldNameText = "";
	private bool _isSanitizing = false;

	public static NewProjectWizard Singleton { get; private set; } = null!;

	public NewProjectWizard()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		_backBtn.Pressed += Back;
		_pathBrowseBtn.Pressed += BrowsePath;
		_createBtn.Pressed += CreateSubmit;
		_templateBtnGroup.Pressed += OnTemplateBtnPressed;
		_projectNameEdit.TextChanged += OnNameEditTextChanged;
		CreateTemplateCard("").ButtonPressed = true;
		ListTemplates();
	}

	private void OnNameEditTextChanged(string newText)
	{
		if (_isSanitizing) return;

		string cleansedName = newText.SanitizeFileName();

		if (cleansedName != newText)
		{
			_isSanitizing = true;
			int caret = _projectNameEdit.CaretColumn;
			_projectNameEdit.Text = cleansedName;
			_projectNameEdit.CaretColumn = Math.Min(caret, cleansedName.Length);
			_isSanitizing = false;
		}

		if (_projectPathEdit.Text.EndsWith(_oldNameText))
		{
			_projectPathEdit.Text = _projectPathEdit.Text.GetBaseDir().PathJoin(cleansedName);
		}

		_oldNameText = cleansedName;
	}

	private void OnTemplateBtnPressed(BaseButton button)
	{
		if (button is TemplatePlaceCard card)
		{
			_targetTemplatePath = card.TemplateFolderPath;
		}
	}

	private void ListTemplates()
	{
		foreach (string p in ProjectManager.GetProjectTemplatePaths())
		{
			CreateTemplateCard(p);
		}
	}

	private TemplatePlaceCard CreateTemplateCard(string p)
	{
		TemplatePlaceCard card = Globals.CreateInstanceFromScene<TemplatePlaceCard>(TemplatesCardPath);
		card.TemplateFolderPath = p;
		card.ButtonGroup = _templateBtnGroup;
		_templatesContainer.AddChild(card);

		return card;
	}

	public void Open()
	{
		Visible = true;
		_oldNameText = DefaultProjectName;
		_projectNameEdit.Text = DefaultProjectName;
		_projectPathEdit.Text = Path.Join(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), CreatorService.PolytoriaFolderName, DefaultProjectName).SanitizePath();
	}

	public void Back()
	{
		Close();
		if (ReturnToSplash)
		{
			ReturnToSplash = false;
			StartupSplash.Singleton.Open();
		}
	}

	public void Close()
	{
		Visible = false;
	}

	private async void CreateSubmit()
	{
		try
		{
			string projName = _projectNameEdit.Text;
			string projPath = _projectPathEdit.Text;
			bool useGit = _gitCheckBtn.ButtonPressed;

			if (string.IsNullOrWhiteSpace(projName))
			{
				CreatorService.Interface.PopupAlert("Please name your project");
				return;
			}

			if (projName != projName.SanitizeFileName())
			{
				CreatorService.Interface.PopupAlert("Project name contains invalid characters");
				return;
			}

			if (string.IsNullOrWhiteSpace(projPath))
			{
				CreatorService.Interface.PopupAlert("Please specify a folder to create project on");
				return;
			}

			if (!Directory.Exists(projPath))
			{
				Directory.CreateDirectory(projPath);
			}
			else if (Directory.GetFiles(projPath).Length != 0 || Directory.GetDirectories(projPath).Length != 0)
			{
				bool confirm = await CreatorService.Interface.PromptConfirmation("There are already files in this folder. Proceed anyway?");
				if (!confirm) return;
			}
			try
			{
				if (string.IsNullOrEmpty(_targetTemplatePath))
				{
					await ProjectManager.NewProject(projPath, new() { ProjectName = projName });
				}
				else
				{
					await ProjectManager.NewProjectFromTemplate(projPath, _targetTemplatePath, new() { ProjectName = projName });
				}
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				CreatorService.Interface.PopupAlert(ex.Message, "Project create failed");
			}

			if (useGit)
			{
				// Initialize git
				try
				{
					await ProjectManager.InitializeGit(projPath);
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					CreatorService.Interface.PopupAlert(ex.Message, "Git initialization Failure");
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert("Failed to create new project");
		}
		Close();
	}

	private void BrowsePath()
	{
		CreatorService.Interface.PromptFileSelect(new()
		{
			Title = "Choose Destination",
			CurrentDirectory = _projectPathEdit.Text.GetBaseDir(),
			DialogMode = DisplayServer.FileDialogMode.OpenDir
		}, OnPathBrowsed);
	}

	private void OnPathBrowsed(string[] paths)
	{
		string path = paths[0];

		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		SetCurrentPath(paths[0]);
	}

	private void SetCurrentPath(string path)
	{
		string oldName = _projectPathEdit.Text.GetFile();

		if (Directory.Exists(path))
		{
			if (new DirectoryInfo(path).Name != DefaultProjectName && Directory.GetFiles(path).Length != 0 || Directory.GetDirectories(path).Length != 0)
			{
				path = path.PathJoin(_projectNameEdit.Text);
			}
		}
		_projectPathEdit.Text = path;

		if (oldName == _projectNameEdit.Text)
		{
			_projectNameEdit.Text = path.GetFile();
		}
	}
}
