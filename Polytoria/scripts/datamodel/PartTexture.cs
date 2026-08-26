// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PartTexture : Instance
{
	private ImageAsset? _asset;
	private Texture2D? _texture;
	private FaceEnum _face;
	private float _transparency;
	private float _exposure;
	private float _contrast = 1f;
	private float _saturation = 1f;
	private float _temperature;

	internal Texture2D? LoadedTexture => _texture;

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			_texture = null;
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(FaceEnum.Top)]
	public FaceEnum Face
	{
		get => _face;
		set
		{
			if (_face == value)
			{
				return;
			}

			_face = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float Transparency
	{
		get => _transparency;
		set
		{
			if (_transparency == value)
			{
				return;
			}

			_transparency = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float Exposure
	{
		get => _exposure;
		set
		{
			if (_exposure == value)
			{
				return;
			}

			_exposure = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Contrast
	{
		get => _contrast;
		set
		{
			if (_contrast == value)
			{
				return;
			}

			_contrast = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Saturation
	{
		get => _saturation;
		set
		{
			if (_saturation == value)
			{
				return;
			}

			_saturation = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0f)]
	public float Temperature
	{
		get => _temperature;
		set
		{
			if (_temperature == value)
			{
				return;
			}

			_temperature = value;
			NotifyParent();
			OnPropertyChanged();
		}
	}

	public override void EnterTree()
	{
		if (Parent is Part part && !part.IsDeleted)
		{
			part.RefreshFaceTextures(include: this);
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		if (Parent is Part part && !part.IsDeleted)
		{
			part.RefreshFaceTextures(exclude: this);
		}
		base.ExitTree();
	}

	public override void PreDelete()
	{
		if (_asset != null)
		{
			_asset.ResourceLoaded -= OnResourceLoaded;
			_asset.UnlinkFrom(this);
			_asset = null;
			_texture = null;
		}
		base.PreDelete();
	}

	public override void HiddenChanged(bool to)
	{
		NotifyParent();
		base.HiddenChanged(to);
	}

	private void NotifyParent()
	{
		if (Parent is Part part && !part.IsDeleted)
		{
			part.RefreshFaceTextures();
		}
	}

	private void OnResourceLoaded(Resource r)
	{
		if (r is Texture2D texture)
		{
			_texture = texture;
			NotifyParent();
		}
	}

	[ScriptEnum("Face")]
	public enum FaceEnum
	{
		Top = 0,
		Bottom = 1,
		Left = 2,
		Right = 3,
		Front = 4,
		Back = 5
	}
}
