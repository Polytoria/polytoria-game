// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Client.UI.Notification;
using Polytoria.Providers.CapturePublish;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Capture")]
public sealed partial class CaptureService : Instance
{
	private const int CaptureCooldownSec = 3;
	private const string CaptureSoundPath = "res://assets/audio/built-in/capture.ogg";

	private Vector2 _photoSizeLimit = new(2000, 2000);
	private bool _debounce = false;

	public ImageTexture? CurrentPhoto = null;
	public string? CurrentPhotoPath = null;

	[ScriptProperty] public bool OnCooldown => _debounce;
	[Editable, ScriptProperty] public bool CanCapture { get; set; } = true;
	[ScriptProperty] public UIField? DefaultCaptureOverlay { get; set; } = null;
	[ScriptProperty] public Dynamic? SpectatorAttach { get; set; } = null;
	internal static ICapturePublisher? CapturePublisher { get; set; }

	private AudioStreamPlayer _shutterSound = null!;
	private Window? _cameraAttach;
	private Camera3D? _spectatorCam;

	public override void Init()
	{
		GDNode.AddChild(_shutterSound = new(), false, Node.InternalMode.Front);
		_shutterSound.Stream = GD.Load<AudioStream>(CaptureSoundPath);

		SetProcess(true);
		base.Init();
	}

	public override void EnterTree()
	{
		Root.Input.GodotInputEvent += OnInput;
		base.EnterTree();
	}

	public override void ExitTree()
	{
		Root?.Input?.GodotInputEvent -= OnInput;
		base.ExitTree();
	}

	public async void TakePhoto()
	{
		if (Root.Environment.CurrentCamera == null) return;
		_debounce = false;
		await TakePhotoAtDynamic(Root.Environment.CurrentCamera);
		// Override debounce
		_debounce = false;
		SaveCurrentPhoto();
	}

	public override void Process(double delta)
	{
		if (_spectatorCam == null || SpectatorAttach == null)
		{
			SetProcess(false);
			return;
		}

		_spectatorCam.GlobalTransform = SpectatorAttach.GetGlobalTransform();
		base.Process(delta);
	}

	public void OpenSpectatorView()
	{
		// Disallow spectator if not attached
		if (SpectatorAttach == null) return;

		if (_cameraAttach == null)
		{
			Window window = new();
			Camera3D cam = new();

			Camera3D activeCam = GDNode.GetViewport().GetCamera3D();

			cam.Fov = activeCam.Fov;
			cam.Projection = activeCam.Projection;

			window.Size = GDNode.GetWindow().Size / 2;
			window.Name = "Spectator View";

			window.AddChild(cam);
			GDNode.AddChild(window, @internal: Node.InternalMode.Back);
			_cameraAttach = window;
			_spectatorCam = cam;

			_cameraAttach.CloseRequested += CameraAttachClose;
		}

		_cameraAttach.PopupCentered();
		SetProcess(true);
	}

	private void CameraAttachClose()
	{
		_cameraAttach?.Visible = false;
		_cameraAttach?.QueueFree();
		_cameraAttach?.CloseRequested -= CameraAttachClose;
		_cameraAttach = null;
		_spectatorCam = null;
		SetProcess(false);
	}

	public void SaveCurrentPhoto()
	{
		if (CurrentPhoto == null) return;
		DateTime time = DateTime.Now;
		string formattedTime = time.ToString("yyyyMMdd-hhmmss");
		string filename = "PolytoriaScreenshot-" + formattedTime + ".png";
		string baseFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures).PathJoin("Polytoria");
		if (!DirAccess.DirExistsAbsolute(baseFolder))
		{
			DirAccess.MakeDirRecursiveAbsolute(baseFolder);
		}
		string photoPath = baseFolder.PathJoin(filename);
		CurrentPhotoPath = photoPath;
		CurrentPhoto.GetImage().SavePng(photoPath);
	}

	public async void UploadCurrentPhoto(string caption = "")
	{
		if (CurrentPhoto == null) return;
		if (CapturePublisher == null) throw new MissingComponentException("Missing capture publisher component");

		byte[] screenshotBytes = CurrentPhoto.GetImage().SavePngToBuffer();

		await CapturePublisher.Publish(screenshotBytes, caption, true);
	}

	public void ViewCurrentPhoto()
	{
		Root.CoreUI.CoreUI.CapturePreview.Open();
	}

	public void OpenCurrentPhotoFile()
	{
		if (CurrentPhotoPath == null) return;
		OS.ShellOpen(CurrentPhotoPath);
	}

	[ScriptMethod]
	public Task TakePhotoAtDynamic(Dynamic dyn, Vector2? photoSize = null, UIField? overlay = null)
	{
		return TakePhotoAt(dyn.Position, dyn.Rotation, photoSize, overlay);
	}

	[ScriptMethod]
	public async Task TakePhotoAt(Vector3 pos, Vector3 rot, Vector2? photoSize = null, UIField? overlay = null)
	{
		if (_debounce) throw new Exception("TakePhoto is on cooldown");
		if (!CanCapture)
		{
			Root.CoreUI.CoreUI.NotificationCenter.FireMessage("Capture is disabled at this time");
			return;
		}
		_debounce = true;
		PrePhotoTake();

		overlay ??= DefaultCaptureOverlay;

		CurrentPhotoPath = null;
		SubViewport subview = new();
		Node3D pivot = new();
		Camera3D cam = new();

		Camera3D activeCam = GDNode.GetViewport().GetCamera3D();

		cam.Fov = activeCam.Fov;
		cam.Projection = activeCam.Projection;

		pivot.AddChild(cam);
		subview.AddChild(pivot);
		GDNode.AddChild(subview, @internal: Node.InternalMode.Back);

		GUI? guiOverlay = null;

		if (overlay != null)
		{
			guiOverlay = New<GUI>();
			UIField clone = (UIField)overlay.Clone();
			clone.Visible = true;
			clone.Parent = guiOverlay;
			guiOverlay.Parent = this;

			// Wait one frame for all node control to init
			await Globals.Singleton.WaitFrame();

			// Override parent check for visible
			foreach (Instance des in guiOverlay.GetDescendants())
			{
				if (des is UIField field)
				{
					field.OverrideParentCheck = true;
					field.RecomputeVisible();
				}
			}

			guiOverlay.GDNode.Reparent(subview);
		}

		pivot.GlobalPosition = pos;
		pivot.GlobalRotationDegrees = rot.FlipEuler();
		cam.RotationDegrees = new Vector3(0, 180, 0);
		if (photoSize != null && photoSize != Vector2.Zero && !(photoSize > _photoSizeLimit))
		{
			subview.Size = (Vector2I)photoSize;
		}
		else
		{
			subview.Size = Globals.Singleton.GetWindow().Size;
		}

		subview.RenderTargetClearMode = SubViewport.ClearMode.Once;
		subview.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

		await Globals.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

		guiOverlay?.Delete();

		Image img = subview.GetTexture().GetImage();
		img.FixAlphaEdges();
		img.GenerateMipmaps();

		CurrentPhoto = ImageTexture.CreateFromImage(img);

		PostPhotoTaken();
	}

	private async void PostPhotoTaken()
	{
		Root.CoreUI.CoreUI.NotificationCenter.FireNotification(
			UINotification.NotificationType.Screenshot,
			new UIScreenshotNotification.ScreenshotNotifyPayload()
			{
				Icon = CurrentPhoto
			}
		);
		await Globals.Singleton.WaitAsync(CaptureCooldownSec);
		_debounce = false;
	}


	private void PrePhotoTake()
	{
		_shutterSound.Play();
	}

	public void OnInput(InputEvent @event)
	{
		if (@event.IsActionPressed("screenshot"))
		{
			TakePhoto();
		}
	}
}
