// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Mobile.UI;

public partial class AvatarPreview : TextureRect
{
	private const string WaveAnimation = "emote_wave";

	[Export] private SubViewport _viewport = null!;
	[Export] private Vector3 _modelRotation = new(0, -20, 0);

	private PolytorianModel _model = null!;
	private int _shownUserID;
	private int _requestedUserID;

	public override void _Ready()
	{
		Texture = _viewport.GetTexture();
		Resized += UpdateViewportSize;
		VisibilityChanged += UpdateRendering;
		UpdateViewportSize();
		UpdateRendering();

		_model = new();
		_viewport.AddChild(_model.GDNode);
		_model.InitEntry();
		_model.Rotation = _modelRotation;
		_model.AvatarLoaded += OnAvatarLoaded;
		_model.PlayIdle();
	}

	public override void _ExitTree()
	{
		_model.AvatarLoaded -= OnAvatarLoaded;
		_model.Delete();
		base._ExitTree();
	}

	public void LoadUser(int userID)
	{
		if (userID == _shownUserID)
		{
			return;
		}

		_requestedUserID = userID;
		_model.LoadAppearance(userID, loadTool: false);
	}

	public void Wave()
	{
		_model.Animator?.PlayOneShotAnimation(WaveAnimation);
	}

	private void UpdateRendering()
	{
		bool visible = IsVisibleInTree();
		_viewport.RenderTargetUpdateMode = visible ? SubViewport.UpdateMode.WhenParentVisible : SubViewport.UpdateMode.Disabled;
		_viewport.ProcessMode = visible ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
	}

	private void UpdateViewportSize()
	{
		float screenScale = GetViewport().GetFinalTransform().Scale.X;
		Vector2I pixelSize = (Vector2I)(Size * screenScale).Round();
		if (pixelSize.X > 0 && pixelSize.Y > 0)
		{
			_viewport.Size = pixelSize;
		}
	}

	private void OnAvatarLoaded()
	{
		_shownUserID = _requestedUserID;

		if (IsVisibleInTree())
		{
			Wave();
		}
	}
}
