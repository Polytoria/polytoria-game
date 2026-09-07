// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Creator.UI.Layout;

public static class DockLayoutService
{
	private const string LayoutPath = "user://layout.cfg";
	private const string Section = "layout";

	private static ConfigFile? _cache;

	private static ConfigFile GetFile()
	{
		if (_cache != null) return _cache;

		_cache = new ConfigFile();
		_cache.Load(LayoutPath);

		return _cache;
	}

	private static void Flush() => GetFile().Save(LayoutPath);

	public static bool GetCollapsed(string panelId, bool defaultValue = false)
	{
		return (bool)GetFile().GetValue(
			Section,
			$"{panelId}_collapsed",
			defaultValue);
	}

	public static void SetCollapsed(string panelId, bool collapsed)
	{
		GetFile().SetValue(
			Section,
			$"{panelId}_collapsed",
			collapsed);

		Flush();
	}

	public static int GetExpandedSize(string panelId, int defaultValue)
	{
		return (int)GetFile().GetValue(
			Section,
			$"{panelId}_size",
			defaultValue);
	}

	public static void SetExpandedSize(string panelId, int size)
	{
		GetFile().SetValue(
			Section,
			$"{panelId}_size",
			size);

		Flush();
	}

	public static void SaveDockLayout(DockLayoutData data)
	{
		string json = JsonSerializer.Serialize(
			data,
			DockLayoutJsonContext.Default.DockLayoutData);

		GetFile().SetValue(Section, "dock_layout", json);
		Flush();
	}

	public static DockLayoutData? LoadDockLayout()
	{
		if (!GetFile().HasSectionKey(Section, "dock_layout"))
			return null;

		string json = (string)GetFile().GetValue(
			Section,
			"dock_layout",
			"");

		if (string.IsNullOrEmpty(json))
			return null;

		try
		{
			return JsonSerializer.Deserialize(
				json,
				DockLayoutJsonContext.Default.DockLayoutData);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static void ResetToDefaults()
	{
		_cache = new ConfigFile();
		Flush();
	}
}

public sealed class DockLayoutData
{
	public Dictionary<string, DockZoneData> Zones { get; set; } = [];

	public Dictionary<string, bool> RegionSplitModes { get; set; } = [];
}

public sealed class DockZoneData
{
	public List<string> PanelIds { get; set; } = [];

	public int ActiveIndex { get; set; }
}

[JsonSerializable(typeof(DockLayoutData))]
[JsonSerializable(typeof(DockZoneData))]
internal partial class DockLayoutJsonContext : JsonSerializerContext
{
}
