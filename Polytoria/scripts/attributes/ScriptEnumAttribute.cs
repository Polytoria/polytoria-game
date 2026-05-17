// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

[AttributeUsage(AttributeTargets.Enum)]
public sealed class ScriptEnumAttribute(string? name = null) : Attribute
{
	public string? Name { get; set; } = name;
	public bool IsCreatorOnly { get; set; }
}
