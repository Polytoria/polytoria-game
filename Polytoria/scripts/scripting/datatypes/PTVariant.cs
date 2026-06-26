// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System;

namespace Polytoria.Scripting.Datatypes;

// PTVariant bridges Godot's universal Variant container into scripting. It wraps a single marshallable scalar/struct
// value; anything else (e.g. dictionaries that could encode a method-call key) is rejected. I am not yet comfortable
// implementing logic that marshalls method-call, as it will require extensive testing.

public class PTVariant : IScriptGDObject
{
	// -----------------------------------------------------------------------------------------------------------------
	// Internal Data

	private Variant variant;

	// -----------------------------------------------------------------------------------------------------------------
	// Exposed Properties

	/// <summary>
	/// The value to assign to the <c>Variant</c>.
	/// </summary>
	[ScriptProperty] public object? Value => ToScript(variant);

	// -----------------------------------------------------------------------------------------------------------------
	// Exposed Scripting Methods

	/// <summary>
	/// Create a new <c>Variant</c> holding <c>nil</c>.
	/// </summary>
	/// <returns>The newly created <c>Variant</c>.</returns>
	[ScriptMethod]
	public static PTVariant New()
	{
		return new();
	}

	/// <summary>
	/// Create a new <c>Variant</c> holding the provided object.
	/// </summary>
	/// <param name="value">The value to assign within the new <c>Variant</c>.</param>
	/// <returns>The newly created <c>Variant</c>.</returns>
	[ScriptMethod]
	public static PTVariant New(object? value)
	{
		return new()
		{
			variant = ToGodot(value)
		};
	}

	/// <summary>
	/// Generate a <c>String</c> describing the target <c>Variant</c>.
	/// </summary>
	/// <returns>A string describing the target <c>Variant</c>.</returns>
	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(PTVariant? v)
	{
		if (v == null) return "<Variant>";
		return $"<Variant:{v.variant}>";
	}

	// -----------------------------------------------------------------------------------------------------------------
	// Exposed Enumerations

	// The enum values match Godots enum (as of 4.7.0), but marshalling facilitates conversion in case they change.
	/// <summary>
	/// Variant types that are accepted by the scripting API.
	/// </summary>
	public enum VariantEnum
	{
		/// <summary>
		/// The <c>Variant</c> contains <c>nil</c>.
		/// </summary>
		Nil = 0,

		/// <summary>
		/// The <c>Variant</c> contains a <c>bool</c>.
		/// </summary>
		Bool = 1,

		/// <summary>
		/// The <c>Variant</c> contains an <c>int</c>.
		/// </summary>
		Int = 2,

		/// <summary>
		/// The <c>Variant</c> contains a <c>float</c>.
		/// </summary>
		Float = 3,

		/// <summary>
		/// The <c>Variant</c> contains a <c>string</c>.
		/// </summary>
		String = 4,

		/// <summary>
		/// The <c>Variant</c> contains a <c>Vector2</c>.
		/// </summary>
		Vector2 = 5,

		/// <summary>
		/// The <c>Variant</c> contains a <c>Vector3</c>.
		/// </summary>
		Vector3 = 9,

		/// <summary>
		/// The <c>Variant</c> contains a <c>Quaternion</c>.
		/// </summary>
		Quaternion = 15,

		/// <summary>
		/// The <c>Variant</c> contains a <c>Color</c>.
		/// </summary>
		Color = 20
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
			or Variant.Type.Int
			or Variant.Type.Float
			or Variant.Type.String
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
			Variant v => v,
			null => new Variant(),
			bool v => v,
			int v => v,
			float v => v,
			string v => v,
			Vector2 v => v,
			Vector3 v => v,
			Quaternion v => v,
			Color v => v,
			_ => throw new ArgumentException(
				$"Unsupported conversion of scripting value to Godot Variant: {value.GetType().Name}")
		};
	}

	public static VariantEnum ToScriptEnum(Variant.Type type) => type switch
	{
		Variant.Type.Bool => VariantEnum.Bool,
		Variant.Type.Int => VariantEnum.Int,
		Variant.Type.Float => VariantEnum.Float,
		Variant.Type.String => VariantEnum.String,
		Variant.Type.Vector2 => VariantEnum.Vector2,
		Variant.Type.Vector3 => VariantEnum.Vector3,
		Variant.Type.Quaternion => VariantEnum.Quaternion,
		Variant.Type.Color => VariantEnum.Color,
		_ => VariantEnum.Nil
	};

	public static Variant.Type ToGodotEnum(VariantEnum type) => type switch
	{
		VariantEnum.Bool => Variant.Type.Bool,
		VariantEnum.Int => Variant.Type.Int,
		VariantEnum.Float => Variant.Type.Float,
		VariantEnum.String => Variant.Type.String,
		VariantEnum.Vector2 => Variant.Type.Vector2,
		VariantEnum.Vector3 => Variant.Type.Vector3,
		VariantEnum.Quaternion => Variant.Type.Quaternion,
		VariantEnum.Color => Variant.Type.Color,
		_ => Variant.Type.Nil
	};
}
