// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Shared;
using Polytoria.Scripting;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Vitals : Instance
{
	private float _maxHealth = 100;
	private float _health = 100;
	private bool _isdead = false;

	public PTSignal<float, float> HealthChanged { get; private set; } = new();

	public PTSignal Died { get; private set; } = new();

	[Editable, ScriptProperty]
	public float Health
	{
		get => _health;
		set
		{
			float oldHealth = _health;
			_health = Mathf.Min(value, MaxHealth);
			if (_health <= 0 && !IsDead)
			{
				_isdead = true;
				Died.Invoke();
			}
			OnPropertyChanged();
			if (_health != oldHealth)
			{
				HealthChanged.Invoke(_health, oldHealth);
			}
		}
	}

	[Editable, ScriptProperty]
	public float MaxHealth
	{
		get => _maxHealth;
		set
		{
			_maxHealth = value;
			if (Health > value) Health = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public bool IsDead
	{
		get => _isdead;
		set
		{
			if (_isdead != value)
			{
				if (value)
				{
					Kill();
				}
				else
				{
					Reset();
				}
				OnPropertyChanged();
			}
		}
	}

	[ScriptMethod]
	public void TakeDamage(float dmg)
	{
		Health -= dmg;
	}

	[ScriptMethod]
	public void Heal(float amount)
	{
		Health += amount;
	}

	[ScriptMethod]
	public void Reset()
	{
		_isdead = false;
		Health = MaxHealth;
	}

	[ScriptMethod]
	public void Reset(float target)
	{
		_isdead = false;
		Health = target;
	}

	[ScriptMethod]
	public void Kill()
	{
		if (!IsDead)
		{
			Health = 0;
			RpcId(1, nameof(NetKill));
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetKill()
	{
		Health = 0;
	}
}
