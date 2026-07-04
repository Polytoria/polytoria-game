// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting.Datatypes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class VariantValue : ValueBase
{
	private Variant _value = PTVariant.ToGodot(null);

	[Editable, ScriptProperty]
	public Variant Value
	{
		get => _value;
		set
		{
			object? oldValue = _value.Obj;
			_value = value;
			if (oldValue != _value.Obj)
			{
				InvokeChanged();
			}
			OnPropertyChanged();
		}
	}
}
