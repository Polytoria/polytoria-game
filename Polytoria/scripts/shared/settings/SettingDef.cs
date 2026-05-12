// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Shared.Settings;

public interface ISettingOption
{
	object? UntypedValue { get; }
	string Label { get; }
	string Description { get; }
}

public class SettingOption<T> : ISettingOption
{
	public required T Value { get; init; }
	public required string Label { get; init; }
	public string Description { get; init; } = string.Empty;
	public object? UntypedValue => Value;
}

public abstract class SettingDef
{
	public required string Key { get; init; }
	public required string SectionKey { get; init; }
	public required string Label { get; init; }
	public string Description { get; init; } = string.Empty;

	public required SettingValueKind ValueKind { get; init; }
	public required SettingControlKind ControlKind { get; init; }

	public bool RequiresRestart { get; init; }
	public bool IsAdvanced { get; init; }

	public abstract object UntypedDefault { get; }
	public abstract Type ValueType { get; }
	public virtual object? UntypedMinValue => null;
	public virtual object? UntypedMaxValue => null;
	public virtual object? UntypedStep => null;
	public virtual IReadOnlyList<ISettingOption>? UntypedOptions => null;
	public abstract object ConvertToType(object? value);

	public virtual void Validate() { }

	public static void ValidateAll(IEnumerable<SettingDef> definitions)
	{
		foreach (var def in definitions)
			def.Validate();
	}
}

public class SettingDef<T> : SettingDef
{
	public required T DefaultValue { get; init; }
	public T? MinValue { get; init; }
	public T? MaxValue { get; init; }
	public T? Step { get; init; }
	public IReadOnlyList<SettingOption<T>>? Options { get; init; }

	public override object UntypedDefault => DefaultValue!;
	public override Type ValueType => typeof(T);
	public override object? UntypedMinValue => MinValue;
	public override object? UntypedMaxValue => MaxValue;
	public override object? UntypedStep => Step;
	private IReadOnlyList<ISettingOption>? _cachedUntypedOptions;
	public override IReadOnlyList<ISettingOption>? UntypedOptions
		=> _cachedUntypedOptions ??= Options?.Cast<ISettingOption>().ToArray();

	public override object ConvertToType(object? value)
	{
		if (value == null)
		{
			return DefaultValue!;
		}

		if (value is T typed)
		{
			return typed;
		}

		if (typeof(T).IsEnum)
		{
			if (value is string stringValue)
			{
				return Enum.Parse(typeof(T), stringValue, true);
			}

			return Enum.ToObject(typeof(T), value);
		}

		return (T)Convert.ChangeType(value, typeof(T));
	}

	public override void Validate()
	{
		Type t = typeof(T);

		SettingValueKind expectedKind = t switch
		{
			_ when t == typeof(bool) => SettingValueKind.Bool,
			_ when t == typeof(int) => SettingValueKind.Int,
			_ when t == typeof(float) => SettingValueKind.Float,
			_ when t == typeof(string) => SettingValueKind.String,
			_ when t.IsEnum => SettingValueKind.Enum,
			_ => throw new InvalidOperationException($"SettingDef<T> has unsupported type {t.Name}.")
		};

		if (ValueKind != expectedKind)
			throw new InvalidOperationException(
				$"Setting '{Key}': ValueKind is {ValueKind} but generic type T is {t.Name}. Expected ValueKind.{expectedKind}.");
		if (MinValue is not null && MaxValue is not null && MinValue is IComparable<T> minComp)
		{
			if (minComp.CompareTo(MaxValue) > 0)
				throw new InvalidOperationException(
					$"Setting '{Key}': MinValue ({MinValue}) is greater than MaxValue ({MaxValue}).");
		}
	}
}
