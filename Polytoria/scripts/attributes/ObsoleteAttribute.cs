// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.CompilerServices;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this as obsolete. Maps to Luau's <c>@deprecated</c> attribute:
/// <see href="https://rfcs.luau.org/syntax-attribute-functions-deprecated.html" />
/// </summary>
/// <param name="reason">The reason for obsoletion</param>
/// <param name="use">What should be used instead</param>
[AttributeUsage(AttributeTargets.All)]
public sealed class ObsoleteAttribute(string? reason = null, string? use = null, [CallerMemberName] string name = "") : Attribute
{
	public readonly string Name = name;
	public readonly string? Reason = reason;
	public readonly string? UseInstead = use;

	public string GetWarning()
	{
		string result = $"'{Name}' is obsolete";
		if (UseInstead != null)
		{
			result += $", use '{UseInstead}' instead";
		}
		if (Reason != null)
		{
			result += $". {Reason}";
		}
		return result;
	}
}
