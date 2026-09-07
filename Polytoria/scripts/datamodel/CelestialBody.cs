// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class CelestialBody : Light
{
	private ImageAsset? _image;
	private Color _haloColor = new(0.8970588f, 0.7760561f, 0.6661981f, 1);
	private float _haloExponent = 125;
	private float _haloContribution = 0.75f;

	public bool CastsLight => GDLight != null; // True only for Sun/Moon

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		float uniform = Mathf.Max(newSize.X, Mathf.Max(newSize.Y, newSize.Z));
		if (newSize.X != uniform || newSize.Y != uniform || newSize.Z != uniform)
		{
			NodeSize = new Vector3(uniform, uniform, uniform);
			return;
		}
		base.OnNodeSizeChanged(newSize);
		Root.Lighting.UpdateSky();
	}

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _image;
		set
		{
			if (_image != null)
			{
				_image.ResourceLoaded -= OnImageLoaded;
				_image.UnlinkFrom(this);
			}
			_image = value;
			if (_image != null)
			{
				_image.LinkTo(this);
				_image.ResourceLoaded += OnImageLoaded;
				if (!_image.IsResourceLoaded) _image.QueueLoadResource();
			}
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	private void OnImageLoaded(Godot.Resource _) => Root.Lighting.UpdateSky();

	[Editable, ScriptProperty]
	public Color HaloColor
	{
		get => _haloColor;
		set
		{
			_haloColor = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float HaloExponent
	{
		get => _haloExponent;
		set
		{
			_haloExponent = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float HaloContribution
	{
		get => _haloContribution;
		set
		{
			_haloContribution = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode() => new Node3D();

	public override void Init()
	{
		base.Init();
		Root.Lighting.RegisterBody(this);
	}

	public override void PreDelete()
	{
		if (_image != null) _image.ResourceLoaded -= OnImageLoaded;
		Root.Lighting.UnregisterBody(this);
		base.PreDelete();
	}
}
