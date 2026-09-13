// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Static]
public sealed partial class Clouds : Instance
{
	private bool _enabled = true;
	private float _coverage = 0.5f;
	private float _softness = 0.5f;
	private float _scale = 1.0f;
	private Vector2 _speed = new(0.05f, 0f);
	private Color _color = new(1, 1, 1, 0.8f);

	[Editable, ScriptProperty]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Coverage
	{
		get => _coverage;
		set
		{
			_coverage = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Softness
	{
		get => _softness;
		set
		{
			_softness = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Scale
	{
		get => _scale;
		set
		{
			_scale = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Speed
	{
		get => _speed;
		set
		{
			_speed = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}
	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			Root.Lighting.UpdateSky();
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode() => new Node();
}
