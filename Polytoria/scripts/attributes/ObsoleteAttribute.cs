// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this as obsolete. Maps to Luau's <c>@deprecated</c> attribute
/// </summary>
/// <param name="reason">The reason for obsoletion</param>
/// <param name="use">The name of what should be used instead</param>
[AttributeUsage(AttributeTargets.All)]
public sealed class ObsoleteAttribute(string? reason = null, string? use = null) : Attribute
{
	public readonly string? Reason = reason;
	public readonly string? UseInstead = use;
}
