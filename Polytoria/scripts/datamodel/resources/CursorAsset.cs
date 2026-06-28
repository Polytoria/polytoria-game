// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel.Resources;

[Abstract]
public partial class CursorAsset : ResourceAsset
{
	public Vector2 Hotspot { get; private set; } = new(0, 0);
	public PTSignal CursorAdjustInternal { get; private set; } = new();

	[Editable, ScriptProperty]
	public Vector2 CursorHotspot
	{
		get => Hotspot;
		set
		{
			Hotspot = value;
			CursorAdjustInternal.Invoke();
			OnPropertyChanged();
		}
	}
	
	public override void PreDelete()
	{
		CursorAdjustInternal.DisconnectAll();
		base.PreDelete();
	}
}
