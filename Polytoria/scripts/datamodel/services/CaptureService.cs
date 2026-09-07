// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;
using Polytoria.Client.UI.Notification;
using Polytoria.Enums;
using Polytoria.Providers.CapturePublish;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Capture")]
public sealed partial class CaptureService : Instance
{
	//Which optional UI layers the player chose to include into the final image
	public readonly struct LayerSelection
	{
		public bool CoreUi { get; init; }
		public bool GameUi { get; init; }

		public static readonly LayerSelection All = new() { CoreUi = true, GameUi = true };
	}

	private const int CaptureCooldownSec = 3;
	private const string CaptureSoundPath = "res://assets/audio/built-in/capture.ogg";

	private Vector2 _photoSizeLimit = new(2000, 2000);
	private bool _debounce = false;
	private bool _isQuickScreenshot = false;
	private LayerSelection? _layerPreferences = null;

	public ImageTexture? CurrentPhoto = null;
	public string? CurrentPhotoPath = null;
	public ImageTexture? CoreUiLayer { get; private set; }
	public ImageTexture? GameUiLayer { get; private set; }

	[ScriptProperty] public bool OnCooldown => _debounce;
	[Editable, ScriptProperty] public bool CanCapture { get; set; } = true;
	[ScriptProperty] public UIField? DefaultCaptureOverlay { get; set; } = null;
	[ScriptProperty] public Dynamic? SpectatorAttach { get; set; } = null;
	[Editable, ScriptProperty] public CaptureLayersEnum DefaultCaptureLayers { get; set; } = CaptureLayersEnum.All;
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

		bool wasQuick = _isQuickScreenshot;
		_isQuickScreenshot = false;

		//Save on the spot if this is a quick screenshot, otheriwse trigger preview
		if (wasQuick)
		{
			SaveCurrentPhoto(CurrentLayerPreferences);
			PostPhotoTaken();
		}
		else
		{
			Root.CoreUI.CoreUI.CapturePreview.Open();
		}
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

	// Remembers the chosen layers for future quick screenshots.
	public void PersistLayerPreferences(LayerSelection selection)
	{
		_layerPreferences = selection;
	}

	// Falls back to the dev-configured default until the player explicitly
	// picks a selection via the preview.
	private LayerSelection CurrentLayerPreferences => _layerPreferences ?? LayersFromEnum(DefaultCaptureLayers);

	private static LayerSelection LayersFromEnum(CaptureLayersEnum layers) => layers switch
	{
		CaptureLayersEnum.None => new LayerSelection(),
		CaptureLayersEnum.CoreOnly => new LayerSelection { CoreUi = true },
		CaptureLayersEnum.GameOnly => new LayerSelection { GameUi = true },
		_ => LayerSelection.All
	};

	// Flattens the base photo with whichever layers are selected.
	private Image ComposeFinalImage(LayerSelection selection)
	{
		Image final = (Image)CurrentPhoto!.GetImage().Duplicate();

		final.ClearMipmaps();
		if (final.GetFormat() != Image.Format.Rgba8)
		{
			final.Convert(Image.Format.Rgba8);
		}

		if (selection.CoreUi && CoreUiLayer != null)
		{
			Image layer = CoreUiLayer.GetImage();
			final.BlendRect(layer, new Rect2I(Vector2I.Zero, layer.GetSize()), Vector2I.Zero);
		}
		if (selection.GameUi && GameUiLayer != null)
		{
			Image layer = GameUiLayer.GetImage();
			final.BlendRect(layer, new Rect2I(Vector2I.Zero, layer.GetSize()), Vector2I.Zero);
		}
		return final;
	}

	// Live preview texture (caller owns and must dispose it).
	public ImageTexture ComposePreview(LayerSelection selection)
	{
		return ImageTexture.CreateFromImage(ComposeFinalImage(selection));
	}

	public void SaveCurrentPhoto(LayerSelection selection)
	{
		if (CurrentPhoto == null) return;
		Image final = ComposeFinalImage(selection);

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
		final.SavePng(photoPath);
	}

	public async void UploadCurrentPhoto(string caption, LayerSelection selection)
	{
		if (CurrentPhoto == null) return;
		if (CapturePublisher == null) throw new MissingComponentException("Missing capture publisher component");

		byte[] screenshotBytes = ComposeFinalImage(selection).SavePngToBuffer();

		await CapturePublisher.Publish(screenshotBytes, caption, true);
	}

	// Cancels the pending photo without saving.
	public void DiscardCurrentPhoto()
	{
		CurrentPhoto?.Dispose();
		CurrentPhoto = null;
		CurrentPhotoPath = null;

		CoreUiLayer?.Dispose();
		CoreUiLayer = null;

		GameUiLayer?.Dispose();
		GameUiLayer = null;
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

	// Wraps a UIField for offscreen rendering
	private async Task<GUI> PrepareOverlayGui(UIField source)
	{
		GUI gui = New<GUI>();
		UIField clone = (UIField)source.Clone();
		clone.Visible = true;
		clone.Parent = gui;
		gui.Parent = this;

		// Wait one frame for all node control to init
		await Globals.Singleton.WaitFrame();

		// Override parent check for visible
		foreach (Instance des in gui.GetDescendants())
		{
			if (des is UIField field)
			{
				field.OverrideParentCheck = true;
				field.RecomputeVisible();
			}
		}

		return gui;
	}

	// GameMenu shares CoreUIRoot's canvas, so a mid-fade close would otherwise
	// show in the captured layer. Hides it for the render and restores it after.
	private async Task<ImageTexture?> CaptureCoreUiLayer(Vector2I size)
	{
		CoreUIRoot? core = CoreUIRoot.Singleton;
		if (core == null) return null;

		bool wasMenuVisible = core.GameMenu.Visible;
		core.GameMenu.Visible = false;

		ImageTexture? result = await CaptureLiveCanvasLayer(core, size);

		core.GameMenu.Visible = wasMenuVisible;

		return result;
	}

	// Renders a live CanvasLayer into an offscreen viewport by attaching its
	// existing rendering-server canvas to a second viewport.
	private async Task<ImageTexture?> CaptureLiveCanvasLayer(CanvasLayer? liveLayer, Vector2I size)
	{
		if (liveLayer == null) return null;

		SubViewport layerView = new()
		{
			Size = size,
			TransparentBg = true,
			RenderTargetClearMode = SubViewport.ClearMode.Once,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Once
		};
		GDNode.AddChild(layerView, @internal: Node.InternalMode.Back);

		Rid canvas = liveLayer.GetCanvas();
		RenderingServer.ViewportAttachCanvas(layerView.GetViewportRid(), canvas);

		await Globals.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

		Image img = layerView.GetTexture().GetImage();
		ImageTexture texture = ImageTexture.CreateFromImage(img);

		RenderingServer.ViewportRemoveCanvas(layerView.GetViewportRid(), canvas);
		layerView.QueueFree();

		return texture;
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
			guiOverlay = await PrepareOverlayGui(overlay);
			guiOverlay.GDNode.Reparent(subview);
		}

		pivot.GlobalPosition = pos;
		pivot.GlobalRotationDegrees = rot;
		cam.RotationDegrees = new Vector3(0, 0, 0);
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

		CurrentPhoto?.Dispose();
		CurrentPhoto = ImageTexture.CreateFromImage(img);

		CoreUiLayer?.Dispose();
		GameUiLayer?.Dispose();
		CoreUiLayer = await CaptureCoreUiLayer(subview.Size);
		GameUiLayer = overlay == null ? await CaptureLiveCanvasLayer(Root.PlayerGUI?.GDNode as CanvasLayer, subview.Size) : null;

		subview.QueueFree();
	}

	private async void PostPhotoTaken()
	{
		ImageTexture icon = ComposePreview(CurrentLayerPreferences);
		Root.CoreUI.CoreUI.NotificationCenter.FireNotification(
			UINotification.NotificationType.Screenshot,
			new UIScreenshotNotification.ScreenshotNotifyPayload()
			{
				Icon = icon
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
			_isQuickScreenshot = true;
			TakePhoto();
		}
	}
}
