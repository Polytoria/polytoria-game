// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this property as editable in Creator
///
/// NOTE: Property marked with this property will be synchronized by the network, to override this. Use NoSync attribute
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EditableAttribute : Attribute
{
	public bool IsHidden { set; get; }
	public string? CustomPropertyControl { set; get; }
}
