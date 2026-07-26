// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.LSP;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Polytoria.Tests;

public class VSCodeConfigServiceTest
{
	[Fact]
	public void MergeRequiredSettings_PreservesUserConfiguration()
	{
		JsonObject settings = new()
		{
			["editor.tabSize"] = 2,
			["luau-lsp.types.definitionFiles"] = new JsonObject
			{
				["@custom"] = "./types/custom.d.luau"
			},
			["luau-lsp.plugins.paths"] = new JsonArray("./plugins/custom.luau"),
			["files.exclude"] = new JsonObject
			{
				["**/*.tmp"] = true
			}
		};

		VSCodeConfigService.MergeRequiredSettings(settings);

		Assert.Equal(2, settings["editor.tabSize"]!.GetValue<int>());
		Assert.Equal("standard", settings["luau-lsp.platform.type"]!.GetValue<string>());
		Assert.False(settings["luau-lsp.sourcemap.enabled"]!.GetValue<bool>());
		Assert.False(settings["luau-lsp.completion.enableFragmentAutocomplete"]!.GetValue<bool>());
		Assert.True(settings["luau-lsp.plugins.enabled"]!.GetValue<bool>());
		Assert.True(settings["luau-lsp.plugins.fileSystem.enabled"]!.GetValue<bool>());

		JsonObject definitions = Assert.IsType<JsonObject>(settings["luau-lsp.types.definitionFiles"]);
		Assert.Equal("./types/custom.d.luau", definitions["@custom"]!.GetValue<string>());
		Assert.Equal("./.poly/luau/def.d.luau", definitions["@poly"]!.GetValue<string>());

		string[] pluginPaths = Assert.IsType<JsonArray>(settings["luau-lsp.plugins.paths"])
			.Select(node => node!.GetValue<string>())
			.ToArray();
		Assert.Contains("./plugins/custom.luau", pluginPaths);
		Assert.Contains("./.poly/luau/polytoria-require.luau", pluginPaths);
		Assert.Contains("./.poly/luau/polytoria-module-types.luau", pluginPaths);

		JsonObject excludedFiles = Assert.IsType<JsonObject>(settings["files.exclude"]);
		Assert.True(excludedFiles["**/*.tmp"]!.GetValue<bool>());
		Assert.True(excludedFiles["**/*.meta"]!.GetValue<bool>());
	}

	[Fact]
	public void Ensure_UpgradesLegacyWorkspaceAndRecommendsLuauExtension()
	{
		string projectDirectory = CreateTemporaryDirectory();
		try
		{
			string vscodeDirectory = Path.Join(projectDirectory, ".vscode");
			Directory.CreateDirectory(vscodeDirectory);
			File.WriteAllText(Path.Join(vscodeDirectory, "settings.json"), """
				{
					// Existing project setting must survive the migration.
					"editor.insertSpaces": false,
					"luau-lsp.platform.type": "standard",
					"luau-lsp.types.definitionFiles": {
						"@poly": "./.poly/luau/def.d.luau",
					},
				}
				""");
			File.WriteAllText(Path.Join(vscodeDirectory, "extensions.json"), """
				{
					"recommendations": ["example.existing-extension"]
				}
				""");

			VSCodeConfigService.Ensure(projectDirectory);

			JsonObject settings = ParseObject(Path.Join(vscodeDirectory, "settings.json"));
			Assert.False(settings["editor.insertSpaces"]!.GetValue<bool>());
			Assert.True(settings["luau-lsp.plugins.enabled"]!.GetValue<bool>());
			Assert.True(settings["luau-lsp.plugins.fileSystem.enabled"]!.GetValue<bool>());
			Assert.False(settings["luau-lsp.completion.enableFragmentAutocomplete"]!.GetValue<bool>());

			JsonObject extensions = ParseObject(Path.Join(vscodeDirectory, "extensions.json"));
			string[] recommendations = Assert.IsType<JsonArray>(extensions["recommendations"])
				.Select(node => node!.GetValue<string>())
				.ToArray();
			Assert.Contains("example.existing-extension", recommendations);
			Assert.Contains("JohnnyMorganz.luau-lsp", recommendations);
		}
		finally
		{
			Directory.Delete(projectDirectory, recursive: true);
		}
	}

	[Fact]
	public void Ensure_IsIdempotent()
	{
		string projectDirectory = CreateTemporaryDirectory();
		try
		{
			VSCodeConfigService.Ensure(projectDirectory);
			string settingsPath = Path.Join(projectDirectory, ".vscode", "settings.json");
			string extensionsPath = Path.Join(projectDirectory, ".vscode", "extensions.json");
			string firstSettings = File.ReadAllText(settingsPath);
			string firstExtensions = File.ReadAllText(extensionsPath);

			VSCodeConfigService.Ensure(projectDirectory);

			Assert.Equal(firstSettings, File.ReadAllText(settingsPath));
			Assert.Equal(firstExtensions, File.ReadAllText(extensionsPath));

			JsonObject settings = ParseObject(settingsPath);
			string[] pluginPaths = Assert.IsType<JsonArray>(settings["luau-lsp.plugins.paths"])
				.Select(node => node!.GetValue<string>())
				.ToArray();
			Assert.Equal(pluginPaths.Length, pluginPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		}
		finally
		{
			Directory.Delete(projectDirectory, recursive: true);
		}
	}

	private static string CreateTemporaryDirectory()
	{
		string path = Path.Join(Path.GetTempPath(), "polytoria-vscode-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static JsonObject ParseObject(string path)
	{
		return Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
	}
}
