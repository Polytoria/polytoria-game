// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Abstract]
public abstract partial class Entity : RigidBody
{
	internal const uint CameraClipCollisionLayerMask = 1u << 5;

	private bool _isSpawn = false;

	private Color _color = new(1, 1, 1);
	private bool _castShadows = true;

	[Editable, ScriptProperty]
	public virtual Color Color
	{
		get => _color;
		set
		{
			if (_color == value)
			{
				return;
			}

			_color = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public virtual bool CastShadows
	{
		get => _castShadows;
		set
		{
			if (_castShadows == value)
			{
				return;
			}

			_castShadows = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IsSpawn
	{
		get => _isSpawn;
		set
		{
			if (_isSpawn == value)
			{
				return;
			}

			_isSpawn = value;

			if (_isSpawn)
			{
				Root.Environment.RegisterSpawnPoint(this);
			}
			else
			{
				Root.Environment.UnregisterSpawnPoint(this);
			}
		}
	}

	public override void Init()
	{
		base.Init();
		UpdateCamLayer();
	}

	public override void PreDelete()
	{
		// Unregister spawnpoint on delete
		Root?.Environment?.UnregisterSpawnPoint(this);
		base.PreDelete();
	}

	internal void UpdateCamLayer()
	{
		ApplyCollisionObjectLayers();
	}

	protected override uint GetAppliedCollisionLayers()
	{
		uint layers = base.GetAppliedCollisionLayers();

		return Color.A > 0.5f
			? layers | CameraClipCollisionLayerMask
			: layers & ~CameraClipCollisionLayerMask;
	}
}
