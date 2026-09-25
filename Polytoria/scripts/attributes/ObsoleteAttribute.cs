// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Polytoria.Attributes;

/// <summary>
/// Maps to Luau's <c>@deprecated</c> attribute:
/// <see href="https://luau.org/attributes/#deprecated" />
/// </summary>
public readonly struct ObsoletionInfo
{
	private readonly string _cachedWarningPart;

	/// <summary>
	/// The reason for obsoletion
	/// </summary>
	public readonly string? Reason;
	/// <summary>
	/// What should be used instead
	/// </summary>
	public readonly string? UseInstead;

	public ObsoletionInfo(string? reason = null, string? use = null)
	{
		Reason = reason;
		UseInstead = use;
		_cachedWarningPart = use != null ? $", use '{use}' instead" : "";
		if (reason != null)
		{
			_cachedWarningPart += $". {reason}";
		}
	}

	public string GetWarning() => "Obsolete" + _cachedWarningPart;

	public string GetWarning(string name) => $"'{name}' is obsolete" + _cachedWarningPart;

	/// <summary>
	/// Generates a Luau <c>@deprecated</c> attribute:
	/// <see href="https://luau.org/attributes/#deprecated" />
	/// </summary>
	public readonly string GetAttribute()
	{
		List<string> args = [];
		if (Reason != null)
		{
			args.Add($"reason = \"{Reason}\"");
		}
		if (UseInstead != null)
		{
			args.Add($"use = \"{UseInstead}\"");
		}
		return args.Count != 0 ? $"@[deprecated {{ {string.Join(", ", args)} }}]" : "@deprecated";
	}

	/// <summary>
	/// Generates a moonwave <c>@deprecated</c> comment for non-functions:
	/// <see href="https://eryn.io/moonwave/docs/TagList/#deprecated" />
	/// </summary>
	public readonly string GetWarningComment()
	{
		string content = GetWarning();
		return content.Length != 0 ? $"--- @deprecated -- {content}" : "--- @deprecated";
	}
}

/// <summary>
/// Mark this as obsolete.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class ObsoleteAttribute(ObsoletionInfo info) : Attribute
{
	public readonly ObsoletionInfo Info = info;

	public ObsoleteAttribute(string? reason = null, string? use = null) : this(new(reason, use)) { }
}
