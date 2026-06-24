// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System;

namespace Polytoria.Scripting.Datatypes;

// PTVariant bridges Godot's universal Variant container into scripting. It wraps a single marshallable
// scalar/struct value; anything else (e.g. dictionaries that could encode a method-call key) is rejected. I am
// not yet comfortable implementing logic that marshalls method-call, as it will require extensive testing.

public class PTVariant : IScriptGDObject
{
	// -----------------------------------------------------------------------------------------------------------------
	// Internal Data

	private Variant variant;

	// -----------------------------------------------------------------------------------------------------------------
	// Exposed Properties

	[ScriptProperty] public object? Value => ToScript(variant);

	// -----------------------------------------------------------------------------------------------------------------
	// Exposed Scripting Methods

	[ScriptMethod]
	public static PTVariant New()
	{
		return new();
	}

	[ScriptMethod]
	public static PTVariant New(object? value)
	{
		return new()
		{
			variant = ToGodot(value)
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(PTVariant? v)
	{
		if (v == null) return "<Variant>";
		return $"<Variant:{v.variant}>";
	}

	// -----------------------------------------------------------------------------------------------------------------
	// Internal Conversions

	// Implicit conversion from ACL type to Godot type.
	public static implicit operator Variant(PTVariant acl) => acl.variant;

	// Implicit conversion from Godot type to ACL type.
	public static implicit operator PTVariant(Variant gd)
	{
		return new PTVariant()
		{
			variant = gd
		};
	}

	// This is here for compatability with existing codebase, implicit conversion is functionally superior.
	public static PTVariant FromGDClass(Variant variant)
	{
		return variant;
	}

	// This is here for compatability with existing codebase, implicit conversion is functionally superior.
	public object ToGDClass()
	{
		return variant;
	}

	// Convert a Godot Variant to a value marshallable to scripting.
	public static object? ToScript(Variant variant) => variant.VariantType switch
	{
		Variant.Type.Nil
			or Variant.Type.Bool
			or Variant.Type.String
			or Variant.Type.Float
			or Variant.Type.Int
			or Variant.Type.Vector2
			or Variant.Type.Vector3
			or Variant.Type.Quaternion
			or Variant.Type.Color => variant.Obj,
		_ => throw new ArgumentException(
			$"Unsupported conversion of Godot Variant to scripting value: {variant.VariantType}")
	};

	// Convert a scripting object to a Godot Variant.
	public static Variant ToGodot(object? value)
	{
		return value switch
		{
			null => new Variant(),
			Variant v => v,
			bool v => v,
			string v => v,
			float v => v,
			int v => v,
			Vector2 v => v,
			Vector3 v => v,
			Quaternion v => v,
			Color v => v,
			_ => throw new ArgumentException(
				$"Unsupported conversion of scripting value to Godot Variant: {value.GetType().Name}")
		};
	}
}
