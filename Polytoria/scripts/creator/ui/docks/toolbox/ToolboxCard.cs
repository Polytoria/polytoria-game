// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Utils;
using Mesh = Polytoria.Datamodel.Mesh;

namespace Polytoria.Creator.UI;

public partial class ToolboxCard : Button
{
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Label _nameLabel = null!;
	[Export] private Label _byLabel = null!;
	[Export] private Control _audioPreView = null!;
	[Export] private BaseButton _playButton = null!;

	private AudioStreamPlayer? _previewSound;

	public APILibraryItem ItemData;
	public LibraryQueryTypeEnum ItemType;
	public Toolbox ToolboxParent = null!;

	public override void _Ready()
	{
		if (ItemType == LibraryQueryTypeEnum.Audio)
		{
			_audioPreView.Visible = true;
			_thumbnailRect.Texture = GD.Load<Texture2D>("res://assets/textures/creator/toolbox/Sound.svg");
		}
		else
		{
			_audioPreView.Visible = false;
			WebAssetLoader.Singleton.GetResource(new() { URL = ItemData.ThumbnailUrl }, r =>
			{
				_thumbnailRect.Texture = (Texture2D)r;
			});
		}
		_nameLabel.Text = ItemData.Name;
		_byLabel.Text = "By " + ItemData.CreatorName;

		_playButton.Toggled += OnPlayToggled;

		base._Ready();
	}

	public override void _GuiInput(InputEvent @event)
	{
		// Right click menu
		if (@event is InputEventMouseButton btn && btn.Pressed && btn.ButtonIndex == MouseButton.Right)
		{
			ToolboxItemContextMenu menu = new() { ItemData = ItemData, ItemType = ItemType, ParentCard = this };
			AddChild(menu);
			menu.PopupAtCursor();
		}
		base._GuiInput(@event);
	}

	private void OnPlayToggled(bool toggledOn)
	{
		if (toggledOn)
		{
			if (_previewSound == null)
			{
				_previewSound = new();

				_previewSound.Finished += () =>
				{
					_playButton.SetPressedNoSignal(false);
				};

				AddChild(_previewSound);
			}
			AssetLoader.Singleton.GetResource(new() { ID = ItemData.ID, Type = ResourceType.Audio }, r =>
			{
				_previewSound.Stream = (AudioStream)r;

				if (ToolboxParent.SoundPreviewingCard != null && IsInstanceValid(ToolboxParent.SoundPreviewingCard) && ToolboxParent.SoundPreviewingCard != this)
				{
					ToolboxParent.SoundPreviewingCard.StopSoundPreview();
				}

				ToolboxParent.SoundPreviewingCard = this;
				_previewSound.Play();
			});
		}
		else
		{
			_previewSound?.Stop();
		}
	}

	public void StopSoundPreview()
	{
		_playButton.SetPressedNoSignal(false);
		ToolboxParent.SoundPreviewingCard = null;
		_previewSound?.Stop();
	}

	public override async void _Pressed()
	{
		if (World.Current == null) { CreatorService.Interface.StatusBar?.SetStatus("No game opened, did not insert"); return; }
		World root = World.Current;
		string nameToUse = ItemData.Name.ToPascalCase().RemoveSymbols();

		switch (ItemType)
		{
			case LibraryQueryTypeEnum.Model:
				{
					Instance? i = await root.Insert.CreatorImportWebModel((int)ItemData.ID, ItemData.Name.ToPascalCase().RemoveSymbols());
					if (i != null)
					{
						i.Parent = root.Environment;
						root.CreatorContext.Selections.SelectOnly(i);

						if (i is Dynamic dyn)
						{
							dyn.Position = root.CreatorContext.Freelook.GetPlacementPosition();
						}
					}
					root.LinkedSession?.RescanFolder();
					break;
				}
			case LibraryQueryTypeEnum.Mesh:
				{
					Mesh mesh = root.New<Mesh>();
					mesh.Name = nameToUse;
					mesh.Parent = root.Environment;
					PTMeshAsset asset = root.New<PTMeshAsset>();
					asset.AssetID = ItemData.ID;
					mesh.Asset = asset;
					mesh.Position = root.CreatorContext.Freelook.GetPlacementPosition();
					root.CreatorContext.Selections.SelectOnly(mesh);
					break;
				}
			case LibraryQueryTypeEnum.Audio:
				{
					Sound sound = root.New<Sound>();
					sound.Name = nameToUse;
					sound.Parent = root.Environment;
					PTAudioAsset asset = root.New<PTAudioAsset>();
					asset.AudioID = ItemData.ID;
					sound.Audio = asset;
					root.CreatorContext.Selections.SelectOnly(sound);
					break;
				}
			case LibraryQueryTypeEnum.Image:
				{
					Image3D img = root.New<Image3D>();
					img.Name = nameToUse;
					img.Parent = root.Environment;
					PTImageAsset asset = root.New<PTImageAsset>();
					asset.ImageID = ItemData.ID;
					img.Image = asset;
					img.Position = root.CreatorContext.Freelook.GetPlacementPosition();
					root.CreatorContext.Selections.SelectOnly(img);
					break;
				}
		}

		base._Pressed();
	}
}
