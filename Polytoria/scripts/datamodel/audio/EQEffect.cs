// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class EQEffect : AudioEffectBase
{
	private BandCountEnum _bandCount = BandCountEnum.Six;

	private AudioEffectEQ? EQ => LiveEffect as AudioEffectEQ;

	[Editable, ScriptProperty, DefaultValue(BandCountEnum.Six)]
	public BandCountEnum BandCount
	{
		get => _bandCount;
		set
		{
			_bandCount = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public int Bands => EQ?.GetBandCount() ?? (int)_bandCount;

	[ScriptMethod]
	public void SetBandGain(int band, float gainDb)
	{
		if (EQ == null || band < 0 || band >= EQ.GetBandCount())
			return;
		EQ.SetBandGainDb(band, gainDb);
	}

	[ScriptMethod]
	public float GetBandGain(int band)
	{
		if (EQ == null || band < 0 || band >= EQ.GetBandCount())
			return 0f;
		return EQ.GetBandGainDb(band);
	}

	protected override AudioEffect CreateEffect()
	{
		return _bandCount switch
		{
			BandCountEnum.Ten => new AudioEffectEQ10(),
			BandCountEnum.TwentyOne => new AudioEffectEQ21(),
			_ => new AudioEffectEQ6()
		};
	}

	public enum BandCountEnum
	{
		Six = 6,
		Ten = 10,
		TwentyOne = 21
	}
}
