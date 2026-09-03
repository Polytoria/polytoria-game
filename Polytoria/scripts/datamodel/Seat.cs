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

	private CharacterModel? _occupant = null;

	[SyncVar, ScriptProperty]
	public CharacterModel? Occupant
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

	[ScriptProperty] public PTSignal<CharacterModel> Sat { get; private set; } = new();
	[ScriptProperty] public PTSignal<CharacterModel> Vacated { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		if (Root.Network.IsServer)
		{
			Touched.Connect(OnSeatTouched);
		}
	}

	internal void InvokeSat(CharacterModel charModel)
	{
		Sat.Invoke(charModel);
	}

	internal void InvokeVacated(CharacterModel charModel)
	{
		Vacated.Invoke(charModel);
	}

	private void OnSeatTouched(Physical hit)
	{
		if (Occupant != null)
		{
			return;
		}
		if (hit is CharacterModel charModel)
		{
			if (!(charModel.Controller is Player ? CanPlayerSit : CanNPCSit)) { return; }
			if (charModel.IsSitting) { return; }
			charModel.Sit(this);
		}
	}
}
