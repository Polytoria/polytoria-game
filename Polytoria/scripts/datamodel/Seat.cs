// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Seat : Part
{
	private bool _canPlayerSit;
	private bool _canNPCSit;
	private bool _sitDirectionLocked;

	private NPC? _occupant = null;

	[SyncVar, ScriptProperty]
	public NPC? Occupant
	{
		get
		{
			if (_occupant != null && _occupant.IsDeleted)
			{
				_occupant = null;
			}
			return _occupant;
		}
		set => _occupant = value;
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool CanPlayerSit
	{
		get => _canPlayerSit;
		set
		{
			_canPlayerSit = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool CanNPCSit
	{
		get => _canNPCSit;
		set
		{
			_canNPCSit = value;
			OnPropertyChanged();
		}
	}
	[Editable, ScriptProperty, DefaultValue(true)]
	public bool SitDirectionLocked
	{
		get => _sitDirectionLocked;
		set
		{
			_sitDirectionLocked = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public PTSignal<NPC> Sat { get; private set; } = new();
	[ScriptProperty] public PTSignal<NPC> Vacated { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		if (Root.Network.IsServer)
		{
			Touched.Connect(OnSeatTouched);
		}
	}

	internal void InvokeSat(NPC npc)
	{
		Sat.Invoke(npc);
	}

	internal void InvokeVacated(NPC npc)
	{
		Vacated.Invoke(npc);
	}

	private void OnSeatTouched(Physical hit)
	{
		if (Occupant != null)
		{
			return;
		}
		if (hit is Player plr)
		{
			if (!CanPlayerSit) { return; }
			plr.Sit(this);
		}
		else if (hit is NPC npc)
		{
			if (!CanNPCSit) { return; }
			npc.Sit(this);
		}
	}
}
