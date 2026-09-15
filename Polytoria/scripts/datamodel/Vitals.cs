// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

public partial class Vitals : Instance
{
	private float _health = 100;
	private float _maxHealth = 100;

	public PTSignal<float, float> HealthChanged { get; private set; } = new();

	public PTSignal Died { get; private set; } = new();

	[Editable, ScriptProperty]
	public float Health
	{
		get => _health;
		set
		{
			if (this is Player plr && !plr.IsReady) return;
			float oldHealth = _health;
			_health = value;
			if (_health <= 0 && !IsDead)
			{
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
			OnPropertyChanged();
		}
	}

	public void TakeDamage(float dmg)
	{
		Health -= dmg;
	}

	public void Heal(float amount)
	{
		Health += amount;
	}

	[ScriptMethod]
	public void Reset()
	{
		Health = MaxHealth;
	}

	[ScriptMethod]
	public void Kill()
	{
		Health = 0;
		RpcId(1, nameof(NetKill));
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetKill()
	{
		Health = 0;
	}
}
