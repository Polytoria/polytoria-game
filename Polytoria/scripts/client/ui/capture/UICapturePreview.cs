// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;

namespace Polytoria.Client.UI.Capture;

public partial class UICapturePreview : Control
{
	public CoreUIRoot CoreUI = null!;

	[Export] private TextureRect _pictureRect = null!;
	[Export] private Button _firstShareBtn = null!;
	[Export] private Button _saveBtn = null!;
	[Export] private Button _firstCloseBtn = null!;
	[Export] private Button _shareBtn = null!;
	[Export] private Button _cancelBtn = null!;
	[Export] private Control _captionWritePage = null!;
	[Export] private Control _firstMenuPage = null!;
	[Export] private TextEdit _captionTextEdit = null!;
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private AnimationPlayer _shareAnimPlay = null!;
	[Export] private Button _coreUiToggle = null!;
	[Export] private Button _gameUiToggle = null!;
	[Export] private VBoxContainer _layerLayout = null!;

	private CaptureService _capture = null!;
	private CaptureService.LayerSelection _selection;
	private ImageTexture? _previewTexture;
	private bool _finalized;

	public override void _Ready()
	{
		_capture = CoreUI.Root.Capture;
		Visible = false;
		_cancelBtn.Pressed += Close;
		_firstCloseBtn.Pressed += Close;
		_saveBtn.Pressed += OnSaveBtnPressed;
		_firstShareBtn.Pressed += OnFirstShareBtnPressed;
		_shareBtn.Pressed += OnShareBtnPressed;
		_coreUiToggle.Toggled += OnCoreUiToggled;
		_gameUiToggle.Toggled += OnGameUiToggled;
		base._Ready();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("toggle_menu"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
		base._UnhandledInput(@event);
	}

	private void OnCoreUiToggled(bool pressed)
	{
		_selection = _selection with { CoreUi = pressed };
		UpdatePreview();
	}

	private void OnGameUiToggled(bool pressed)
	{
		_selection = _selection with { GameUi = pressed };
		UpdatePreview();
	}

	private void UpdatePreview()
	{
		_previewTexture?.Dispose();
		_previewTexture = _capture.ComposePreview(_selection);
		_pictureRect.Texture = _previewTexture;
	}

	private void FinalizeSave()
	{
		_finalized = true;
		_capture.SaveCurrentPhoto(_selection);
		_capture.PersistLayerPreferences(_selection);
	}

	private void OnShareBtnPressed()
	{
		FinalizeSave();
		Close();
		_capture.UploadCurrentPhoto(_captionTextEdit.Text, _selection);
	}

	private void OnFirstShareBtnPressed()
	{
		_shareAnimPlay.Play("share_appear");
		_firstMenuPage.Visible = false;
		_captionWritePage.Visible = true;
	}

	private void OnSaveBtnPressed()
	{
		FinalizeSave();
		Close();
	}

	public void Open()
	{
		CoreUI.CoreUIActive = true;
		_captionTextEdit.Text = "";
		_firstMenuPage.Visible = true;
		_captionWritePage.Visible = false;
		_shareBtn.GrabFocus();

		_finalized = false;

		bool coreAvailable = _capture.CoreUiLayer != null;
		bool gameAvailable = _capture.GameUiLayer != null;
		bool notYetSaved = _capture.CurrentPhotoPath == null;

		_selection = new() { CoreUi = coreAvailable, GameUi = gameAvailable };
		_coreUiToggle.ButtonPressed = _selection.CoreUi;
		_gameUiToggle.ButtonPressed = _selection.GameUi;
		_layerLayout.Visible = (coreAvailable || gameAvailable) && notYetSaved;

		UpdatePreview();

		_saveBtn.Visible = notYetSaved;
		_animPlay.Play("appear");
	}

	public void Close()
	{
		if (!_finalized)
		{
			_capture.DiscardCurrentPhoto();
		}

		_previewTexture?.Dispose();
		_previewTexture = null;

		CoreUI.CoreUIActive = false;
		_animPlay.Play("disappear");
	}
}
