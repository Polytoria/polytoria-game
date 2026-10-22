// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;

namespace Polytoria.Mobile.UI;

public partial class PlaceCard : Button
{
	private const string ScenePath = "res://scenes/mobile/components/shared/place_card.tscn";
	private const string SkeletonPath = "res://scenes/mobile/components/shared/place_card_skeleton.tscn";

	private const float InfoHeight = 66f;

	[Export] private Control _thumbnail = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private Label _gameTitleLabel = null!;
	[Export] private Label _playingLabel = null!;

	public APIWorldsData PlaceData;

	private readonly PTImageAsset _iconAsset = new();
	private Tween? _scaleTween;

	public static PlaceCard Create(APIWorldsData data)
	{
		PlaceCard card = Globals.CreateInstanceFromScene<PlaceCard>(ScenePath);
		card.PlaceData = data;
		return card;
	}

	public static Control CreateSkeleton()
	{
		return Globals.CreateInstanceFromScene<Control>(SkeletonPath);
	}

	public static void SetCardWidth(Control card, float width)
	{
		card.CustomMinimumSize = new(width, width + InfoHeight);
		Control thumbnail = card is PlaceCard placeCard ? placeCard._thumbnail : card.GetNode<Control>("Thumbnail");
		thumbnail.CustomMinimumSize = new(width, width);
	}

	public override void _Ready()
	{
		_iconRect.Resized += () => _iconRect.SetInstanceShaderParameter("size", _iconRect.Size);
		_iconAsset.ResourceLoaded += OnIconLoaded;

		_gameTitleLabel.Text = PlaceData.Name;
		_playingLabel.Text = $"{PlaceData.Playing} active now";

		_iconAsset.ImageType = ImageTypeEnum.PlaceIcon;
		_iconAsset.ImageID = (uint)PlaceData.Id;
		_iconAsset.LoadResource();

		ButtonDown += () => _scaleTween = CardAnimator.Press(this, _scaleTween, true);
		ButtonUp += () => _scaleTween = CardAnimator.Press(this, _scaleTween, false);
		Pressed += OnPressed;
	}

	public void PopIn(int index)
	{
		_scaleTween = CardAnimator.PopIn(this, index);
	}

	private void OnPressed()
	{
		MobileUI.Singleton.SwitchTo(MobileViewEnum.PlaceInfo, PlaceData.Id);
	}

	private void OnIconLoaded(Resource tex)
	{
		_iconRect.Texture = (Texture2D)tex;
	}
}
