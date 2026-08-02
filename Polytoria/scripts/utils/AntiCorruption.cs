// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Polytoria.Utils;

// Godot's API guards bad input with ERR_FAIL_* macros: this prints an error and returns a default value rather than
// raising a catchable error, and several of those paths leave data inconsistent. In worse cases bad inputs can crash
// the engine or expose vulnerabilities. These helpers are for validating every script-supplied argument up front and
// throwing a descriptive exception (the scripting bridge converts the message into a Luau error caught by pcall) before
// the Godot object is ever touched.
//
// Reference:
// - https://github.com/godotengine/godot/blob/4.7/core/error/error_macros.h
//
// Class-specific anti-corruption validations should be directly added to the target class as a private method. Methods
// that are generally reusable between classes should be added here.

public static class AntiCorruption
{
	// ------------------------------------------------- Gating --------------------------------------------------------

	// ScriptProperty setters that use anti-corruption validation logic validate their inputs and throw on invalid
	// values. The same build runs the Creator, where properties are edited live and exceptions must not be thrown. The
	// Creator and Client share the CREATOR binary, so they can only be told apart at runtime. ShouldValidate ensures
	// that validation is skipped unless running as the client, preventing the Creator from throwing an error. Because
	// places are loaded through the property setters (see XmlFormat), this guarantees authored values are validated
	// when the client starts. (In non-Creator builds validation is always on.)
	public static bool ShouldValidate =>
#if CREATOR
		Polytoria.Shared.Globals.CurrentAppEntry == Polytoria.Shared.Globals.AppEntryEnum.Client;
#else
		true;
#endif

	// ------------------------------------------------ Numbers -------------------------------------------------------

	// Ensure finite value.
	public static void ValidateFinite<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	)
	{
		bool valid = value switch
		{
			float v => float.IsFinite(v),
			double v => double.IsFinite(v),
			Godot.Vector2 v => v.IsFinite(),
			Godot.Vector3 v => v.IsFinite(),
			Godot.Quaternion v => v.IsFinite(),
			_ => throw new NotSupportedException($"Finite validation is not supported for type {typeof(T)}.")
		};

		if (!valid)
			throw new ArgumentException($"{paramName} must have finite components (got {value}).", paramName);
	}

	// Ensure value > 0.
	public static void ValidateGTZ<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : INumber<T>
	{
		if (value <= T.Zero)
			throw new ArgumentException($"{paramName} must be greater than 0 (got {value}).", paramName);
	}

	// Ensure value >= 0.
	public static void ValidateGTEZ<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : INumber<T>
	{
		if (value < T.Zero)
			throw new ArgumentException($"{paramName} must be greater than or equal to 0 (got {value}).", paramName);
	}

	// Ensure value < 0.
	public static void ValidateLTZ<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : INumber<T>
	{
		if (value >= T.Zero)
			throw new ArgumentException($"{paramName} must be less than 0 (got {value}).", paramName);
	}

	// Ensure value <= 0.
	public static void ValidateLTEZ<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : INumber<T>
	{
		if (value > T.Zero)
			throw new ArgumentException($"{paramName} must be less than or equal to zero (got {value}).", paramName);
	}

	// Ensure value is non-zero.
	public static void ValidateNEZ<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : INumber<T>
	{
		if (value != T.Zero)
			throw new ArgumentException($"{paramName} must be non-zero.", paramName);
	}

	// -------------------------------------------------- Names --------------------------------------------------------

	// Reject nil/null strings.
	public static void ValidateNameNotNil(
		string name,
		[CallerArgumentExpression(nameof(name))] string? paramName = null)
	{
		if (name is null)
			throw new ArgumentNullException(paramName, $"{paramName} cannot be nil.");
	}

	// Reject empty strings.
	public static void ValidateNameNotEmpty(
		string name,
		[CallerArgumentExpression(nameof(name))] string? paramName = null)
	{
		if (name is { Length: 0 })
			throw new ArgumentException($"{paramName} cannot be empty.", paramName);
	}

	// Reject nil/null and empty strings.
	public static void ValidateName(
		string name,
		[CallerArgumentExpression(nameof(name))] string? paramName = null)
	{
		ValidateNameNotNil(name, paramName);
		ValidateNameNotEmpty(name, paramName);
	}

	// -------------------------------------------------- Enums --------------------------------------------------------

	// Reject enum values outside the defined set.
	public static void ValidateEnum<T>(
		T value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null
	) where T : struct, Enum
	{
		if (!Enum.IsDefined(value))
			throw new ArgumentException($"'{value}' is not a valid {typeof(T).Name} value.", paramName);
	}
}
