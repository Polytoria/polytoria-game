// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Shared.Settings;

public interface ISettingsContext
{
	T Get<T>(string key);
	object? GetUntyped(string key);
	void Set<T>(string key, T value);

	event Action<SettingChangedEvent>? Changed;
}

public readonly record struct SettingChangedEvent(
	string Key,
	object? OldValue,
	object? NewValue,
	bool RequiresRestart
);
