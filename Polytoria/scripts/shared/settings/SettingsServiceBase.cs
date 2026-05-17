// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Shared.Settings;

public abstract partial class SettingsServiceBase : Node, ISettingsContext
{
	protected abstract string SettingsPath { get; }
	protected abstract IReadOnlyDictionary<string, SettingDef> Registry { get; }

	protected readonly Dictionary<string, object?> _values = [];
	private bool _saveQueued;
	private bool _headless;

	public event Action<SettingChangedEvent>? Changed;

	protected SettingsServiceBase()
	{
		_headless = DisplayServer.GetName() == "headless";
		if (!_headless)
			Globals.BeforeQuit += Save;
	}

	public override void _ExitTree()
	{
		if (!_headless)
		{
			Globals.BeforeQuit -= Save;
			_saveQueued = false;
			Save();
		}
		base._ExitTree();
	}

	public virtual T Get<T>(string key)
	{
		if (_values.TryGetValue(key, out object? value) && value is T typed)
			return typed;

		if (Registry.TryGetValue(key, out var def))
			return (T)def.ConvertToType(def.UntypedDefault);

		throw new KeyNotFoundException($"Setting key '{key}' is not registered.");
	}

	public virtual object? GetUntyped(string key)
	{
		if (_values.TryGetValue(key, out object? value))
			return value;

		if (Registry.TryGetValue(key, out var def))
			return def.UntypedDefault;

		throw new KeyNotFoundException($"Setting key '{key}' is not registered.");
	}

	public virtual void Set<T>(string key, T value)
	{
		if (!Registry.TryGetValue(key, out var def))
			throw new KeyNotFoundException($"Setting key '{key}' is not registered.");

		object normalized = def.ConvertToType(value);

		object? oldValue = GetUntyped(key);
		if (Equals(oldValue, normalized))
			return;

		_values[key] = normalized;
		Changed?.Invoke(new SettingChangedEvent(key, oldValue, normalized, def.RequiresRestart));
		OnAfterSet(key, normalized);
		QueueSave();
	}

	protected virtual void OnAfterSet(string key, object normalizedValue)
	{
	}

	protected void Load()
	{
		SettingsFileUtility.Load(SettingsPath, _values, Registry);
	}

	protected void ApplyDefaults()
	{
		foreach (var pair in Registry)
		{
			if (!_values.ContainsKey(pair.Key))
				_values[pair.Key] = pair.Value.UntypedDefault;
		}
	}

	protected void QueueSave()
	{
		if (_saveQueued)
			return;

		_saveQueued = true;
		Callable.From(() =>
		{
			_saveQueued = false;
			Save();
		}).CallDeferred();
	}

	public void Save()
	{
		SettingsFileUtility.Save(SettingsPath, _values);
	}
}
