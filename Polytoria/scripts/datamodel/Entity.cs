// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Abstract]
public abstract partial class Entity : RigidBody
{
	private bool _isSpawn = false;
	private uint _camCollisionLayer = uint.MaxValue;

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
				if (GDRigidBody != null)
				{
					GDRigidBody.LinearVelocity = Vector3.Zero;
					GDRigidBody.AngularVelocity = Vector3.Zero;
				}
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
		UpdateCamLayer();
		base.Init();
	}

	public override void PreDelete()
	{
		// Unregister spawnpoint on delete
		Root?.Environment?.UnregisterSpawnPoint(this);
		base.PreDelete();
	}

	internal void UpdateCamLayer()
	{
		uint targetLayer;
		if (Color.A > 0.5)
		{
			// Set layer for solid
			targetLayer = 1u << 0 | 1u << 5;
		}
		else
		{
			// Set layer for transparent
			targetLayer = 1u;
		}

		if (_camCollisionLayer == targetLayer)
		{
			return;
		}

		_camCollisionLayer = targetLayer;
		GDRigidBody.CollisionLayer = targetLayer;
	}
}
