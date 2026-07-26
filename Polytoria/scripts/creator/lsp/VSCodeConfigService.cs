// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Polytoria.Creator.LSP;

/// <summary>
/// Maintains the workspace settings required for Polytoria's generated Luau
/// definitions and source-transformation plugins to work in VS Code.
/// Existing user settings and extension recommendations are preserved.
/// </summary>
public static class VSCodeConfigService
{
	private const string LuauExtensionId = "JohnnyMorganz.luau-lsp";

	private static readonly string[] RequiredPluginPaths =
	[
		"./.poly/luau/polytoria-require.luau",
		"./.poly/luau/polytoria-module-types.luau"
	];

	private static readonly JsonDocumentOptions DocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true
	};

	public static void Ensure(string projectFolderPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);

		string vscodeDirectory = Path.Join(projectFolderPath, ".vscode");
		Directory.CreateDirectory(vscodeDirectory);

		EnsureSettings(Path.Join(vscodeDirectory, "settings.json"));
		EnsureExtensions(Path.Join(vscodeDirectory, "extensions.json"));
	}

	public static JsonObject MergeRequiredSettings(JsonObject? settings = null)
	{
		settings ??= [];

		settings["luau-lsp.platform.type"] = "standard";
		settings["luau-lsp.sourcemap.enabled"] = false;
		settings["luau-lsp.completion.enableFragmentAutocomplete"] = false;
		settings["luau-lsp.plugins.enabled"] = true;
		settings["luau-lsp.plugins.fileSystem.enabled"] = true;

		JsonObject definitionFiles = GetOrCreateObject(settings, "luau-lsp.types.definitionFiles");
		definitionFiles["@poly"] = "./.poly/luau/def.d.luau";

		JsonArray pluginPaths = GetOrCreateArray(settings, "luau-lsp.plugins.paths");
		foreach (string pluginPath in RequiredPluginPaths)
		{
			AddUniqueString(pluginPaths, pluginPath);
		}

		JsonObject excludedFiles = GetOrCreateObject(settings, "files.exclude");
		excludedFiles["**/*.meta"] = true;

		return settings;
	}

	public static JsonObject MergeExtensionRecommendation(JsonObject? extensions = null)
	{
		extensions ??= [];
		JsonArray recommendations = GetOrCreateArray(extensions, "recommendations");
		AddUniqueString(recommendations, LuauExtensionId);
		return extensions;
	}

	private static void EnsureSettings(string settingsPath)
	{
		JsonObject settings = ReadObjectOrBackup(settingsPath);
		MergeRequiredSettings(settings);
		WriteObject(settingsPath, settings);
	}

	private static void EnsureExtensions(string extensionsPath)
	{
		JsonObject extensions = ReadObjectOrBackup(extensionsPath);
		MergeExtensionRecommendation(extensions);
		WriteObject(extensionsPath, extensions);
	}

	private static JsonObject ReadObjectOrBackup(string path)
	{
		if (!File.Exists(path))
		{
			return [];
		}

		string content = File.ReadAllText(path);
		try
		{
			JsonNode? parsed = JsonNode.Parse(content, nodeOptions: null, documentOptions: DocumentOptions);
			if (parsed is JsonObject parsedObject)
			{
				return parsedObject;
			}
		}
		catch (JsonException)
		{
			// Preserve malformed JSON/JSONC before replacing it with a usable config.
		}

		string backupPath = path + ".polytoria-backup";
		File.Copy(path, backupPath, overwrite: true);
		return [];
	}

	private static JsonObject GetOrCreateObject(JsonObject root, string propertyName)
	{
		if (root[propertyName] is JsonObject existing)
		{
			return existing;
		}

		JsonObject created = [];
		root[propertyName] = created;
		return created;
	}

	private static JsonArray GetOrCreateArray(JsonObject root, string propertyName)
	{
		if (root[propertyName] is JsonArray existing)
		{
			return existing;
		}

		JsonArray created = [];
		root[propertyName] = created;
		return created;
	}

	private static void AddUniqueString(JsonArray array, string value)
	{
		foreach (JsonNode? item in array)
		{
			if (item is JsonValue jsonValue &&
				jsonValue.TryGetValue(out string? existing) &&
				string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}

		JsonNode? node = JsonValue.Create(value);
		array.Add(node);
	}

	private static void WriteObject(string path, JsonObject value)
	{
		string content = value.ToJsonString(SerializerOptions) + Environment.NewLine;
		if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
		{
			return;
		}

		File.WriteAllText(path, content);
	}
}
