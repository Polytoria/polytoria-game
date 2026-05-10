// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Abstract]
public abstract partial class AudioEffectBase : Instance
{
	private bool _enabled = true;
	protected AudioEffect? LiveEffect;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			int idx = FindEffectIndex();
			if (idx >= 0 && Parent is SoundGroup sg)
				AudioServer.SetBusEffectEnabled(sg.BusIndex, idx, _enabled);
			OnPropertyChanged();
		}
	}

	protected abstract AudioEffect CreateEffect();

	protected T? GetLive<T>() where T : AudioEffect => LiveEffect as T;

	private int FindEffectIndex()
	{
		if (LiveEffect == null || Parent is not SoundGroup sg || sg.BusIndex < 0)
			return -1;

		int bus = sg.BusIndex;
		int count = AudioServer.GetBusEffectCount(bus);
		for (int i = 0; i < count; i++)
		{
			if (AudioServer.GetBusEffect(bus, i) == LiveEffect)
				return i;
		}
		return -1;
	}

	private void TryAttachEffect()
	{
		if (LiveEffect != null)
			return;

		if (Parent is not SoundGroup sg || sg.BusIndex < 0)
			return;

		LiveEffect = CreateEffect();
		int bus = sg.BusIndex;
		AudioServer.Lock();
		AudioServer.AddBusEffect(bus, LiveEffect);
		int idx = AudioServer.GetBusEffectCount(bus) - 1;
		AudioServer.SetBusEffectEnabled(bus, idx, _enabled);
		AudioServer.Unlock();
	}

	public override void Init()
	{
		base.Init();
		TryAttachEffect();
	}

	public override void EnterTree()
	{
		base.EnterTree();
		TryAttachEffect();
	}

	private void DetachEffect()
	{
		if (LiveEffect == null)
			return;

		if (Parent is SoundGroup sg && sg.BusIndex >= 0)
		{
			int idx = FindEffectIndex();
			if (idx >= 0)
			{
				AudioServer.Lock();
				AudioServer.RemoveBusEffect(sg.BusIndex, idx);
				AudioServer.Unlock();
			}
		}
		LiveEffect = null;
	}

	public override void ExitTree()
	{
		DetachEffect();
		base.ExitTree();
	}

	public override void PreDelete()
	{
		DetachEffect();
		base.PreDelete();
	}
}
