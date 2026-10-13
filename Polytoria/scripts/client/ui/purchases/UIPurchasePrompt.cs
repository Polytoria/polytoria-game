// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Threading.Tasks;

namespace Polytoria.Client.UI.Purchases;

public partial class UIPurchasePrompt : Control
{
	private const float PurchaseDelaySec = 1.25f;
	private const float PurchaseHoldSec = 1.5f;
	private const float PurchaseHoldReleaseSec = 0.15f;

	[Export] private Label _purchaseText = null!;
	[Export] private Label _priceLabel = null!;
	[Export] private Button _purchaseButton = null!;
	[Export] private Control _purchaseHoldFill = null!;
	[Export] private Control _purchaseHoldDarken = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private AnimationPlayer _animPlay = null!;
	private PTImageAsset? _iconImg;
	private Tween? _holdTween;
	private bool _purchaseTriggered;

	public event Action<bool>? Requested;

	public CoreUIRoot CoreUI = null!;

	public override void _Ready()
	{
		_purchaseButton.ButtonDown += OnPurchaseHoldStart;
		_purchaseButton.ButtonUp += OnPurchaseHoldEnd;
		_cancelButton.Pressed += OnCancel;
		base._Ready();
	}

	public override void _ExitTree()
	{
		_iconImg?.ResourceLoaded -= OnIconImgLoaded;
		base._ExitTree();
	}

	private void OnPurchaseHoldStart()
	{
		_purchaseTriggered = false;

		_holdTween?.Kill();
		_holdTween = CreateTween();
		_holdTween.SetParallel(true);
		_holdTween.TweenProperty(_purchaseHoldFill, "size:x", _purchaseButton.Size.X, PurchaseHoldSec)
			.From(0f);
		_holdTween.TweenProperty(_purchaseHoldDarken, "size:x", _purchaseButton.Size.X, PurchaseHoldSec)
			.From(0f);
		_holdTween.Chain().TweenCallback(Callable.From(OnPurchaseHoldCompleted));
	}

	private void OnPurchaseHoldEnd()
	{
		if (_purchaseTriggered)
			return;

		_holdTween?.Kill();
		_holdTween = CreateTween();
		_holdTween.SetParallel(true);
		_holdTween.TweenProperty(_purchaseHoldFill, "size:x", 0f, PurchaseHoldReleaseSec);
		_holdTween.TweenProperty(_purchaseHoldDarken, "size:x", 0f, PurchaseHoldReleaseSec);
	}

	private void OnPurchaseHoldCompleted()
	{
		_purchaseTriggered = true;
		Requested?.Invoke(true);
		_purchaseButton.Disabled = true;
		_cancelButton.Disabled = true;
	}

	private void OnCancel()
	{
		Requested?.Invoke(false);
		Close();
	}

	public async Task Prompt(APIStoreItem item)
	{
		_purchaseText.Text = $"Would you like to buy {item.Name}?";
		_priceLabel.Text = item.Price!.Value.ToString();

		// Reset button state
		_holdTween?.Kill();
		_purchaseTriggered = false;
		_purchaseHoldFill.Size = new Vector2(0f, _purchaseHoldFill.Size.Y);
		_purchaseHoldDarken.Size = new Vector2(0f, _purchaseHoldDarken.Size.Y);
		_cancelButton.Disabled = false;
		_purchaseButton.Disabled = true;
		_purchaseButton.GrabFocus();

		_iconImg?.ResourceLoaded -= OnIconImgLoaded;
		_iconImg?.Delete();

		_iconRect.Texture = null;

		_iconImg = new();
		_iconImg.ResourceLoaded += OnIconImgLoaded;
		_iconImg.ImageType = ImageTypeEnum.AssetThumbnail;
		_iconImg.ImageID = (uint)item.Id;
		_iconImg.LoadResource();

		_animPlay.Play("RESET");
		await ToSignal(_animPlay, AnimationPlayer.SignalName.AnimationFinished);
		_animPlay.Play("appear");

		await Globals.Singleton.WaitAsync(PurchaseDelaySec);
		_purchaseButton.Disabled = false;
	}

	public async void PlayPurchaseSuccess()
	{
		_animPlay.Play("bought");
		await ToSignal(_animPlay, AnimationPlayer.SignalName.AnimationFinished);
		Close();
	}

	public void Close()
	{
		_animPlay.Play("disappear");
	}

	private void OnIconImgLoaded(Resource resource)
	{
		_iconRect.Texture = (Texture2D)resource;
	}
}
