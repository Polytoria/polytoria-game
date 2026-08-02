// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Datamodel.Data;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class RangeValue : ValueBase
{
	private NumberRange _val = new();

	[Editable, ScriptProperty]
	public NumberRange Value
	{
		get => _val;
		set
		{
			NumberRange oldVal = _val;
			_val = value;
			if (_val.Min != oldVal.Min || _val.Max != oldVal.Max)
			{
				InvokeChanged();
			}
			OnPropertyChanged();
		}
	}
}
