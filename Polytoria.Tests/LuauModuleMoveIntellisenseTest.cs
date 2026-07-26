// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.LSP;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class LuauModuleMoveIntellisenseTest
{
	[Fact]
	public async Task MoveBeforeRequireEditAndExportChangesStayInSync()
	{
		CancellationToken testCancellation = TestContext.Current.CancellationToken;
		using LuauLspTestWorkspace workspace = new("polytoria-module-move-lsp");

		string initialModuleSource = """
			local MathUtil = {}
			MathUtil.Version = "1.0"

			function MathUtil.double(value)
				return value * 2
			end

			return MathUtil
			""";
		string modulePath = workspace.WriteFile("scripts/modules/MathUtil.luau", initialModuleSource);

		string initialServerSource = CreateServerSource("world.ScriptService.MathUtil");
		string serverPath = workspace.WriteFile("scripts/server/Test.server.luau", initialServerSource);
		WriteModuleMap(workspace, "world.ScriptService.MathUtil");

		await RestartAsync(workspace, modulePath, initialModuleSource, serverPath, initialServerSource, testCancellation);

		HashSet<string> labels = await WaitForMathUtilAsync(
			workspace,
			serverPath,
			labels => labels.Contains("Version") && labels.Contains("double"),
			testCancellation);
		Assert.Contains("Version", labels);
		Assert.Contains("double", labels);

		// Reproduce the order that previously broke completion: move the ModuleScript,
		// let Creator's module-map change restart the language server while the source
		// still contains the old require, and only then edit the require expression.
		WriteModuleMap(workspace, "world.Environment.MathUtil");
		await RestartAsync(workspace, modulePath, initialModuleSource, serverPath, initialServerSource, testCancellation);

		string movedServerSource = CreateServerSource("world.Environment.MathUtil");
		workspace.WriteFile("scripts/server/Test.server.luau", movedServerSource);
		await workspace.Client.DidChangeAsync(serverPath, movedServerSource, 2);

		labels = await WaitForMathUtilAsync(
			workspace,
			serverPath,
			labels => labels.Contains("Version") && labels.Contains("double"),
			testCancellation);
		Assert.Contains("Version", labels);
		Assert.Contains("double", labels);

		string addedExportSource = """
			local MathUtil = {}
			MathUtil.Version = "1.1"

			function MathUtil.double(value)
				return value * 2
			end

			function MathUtil.triple(value)
				return value * 3
			end

			return MathUtil
			""";
		workspace.WriteFile("scripts/modules/MathUtil.luau", addedExportSource);

		// LuaCompletionService intentionally restarts the built-in language server
		// when a linked ModuleScript file changes so source-transform plugins rerun.
		await RestartAsync(workspace, modulePath, addedExportSource, serverPath, movedServerSource, testCancellation);

		labels = await WaitForMathUtilAsync(
			workspace,
			serverPath,
			labels => labels.Contains("double") && labels.Contains("triple"),
			testCancellation);
		Assert.Contains("double", labels);
		Assert.Contains("triple", labels);

		string removedExportSource = """
			local MathUtil = {}
			MathUtil.Version = "1.2"

			function MathUtil.triple(value)
				return value * 3
			end

			return MathUtil
			""";
		workspace.WriteFile("scripts/modules/MathUtil.luau", removedExportSource);
		await RestartAsync(workspace, modulePath, removedExportSource, serverPath, movedServerSource, testCancellation);

		labels = await WaitForMathUtilAsync(
			workspace,
			serverPath,
			labels => labels.Contains("triple") && !labels.Contains("double"),
			testCancellation);
		Assert.Contains("triple", labels);
		Assert.DoesNotContain("double", labels);
	}

	private static string CreateServerSource(string moduleWorldPath)
	{
		return $"local MathUtil = require({moduleWorldPath})\nMathUtil.\n";
	}

	private static void WriteModuleMap(LuauLspTestWorkspace workspace, string moduleWorldPath)
	{
		string content = $"""
			# Polytoria Luau module map v2
			# S	world-id	source-file	source-world-path
			# M	world-id	module-world-path	module-file
			S	0	scripts/server/Test.server.luau	world.ScriptService.Test
			M	0	{moduleWorldPath}	scripts/modules/MathUtil.luau
			""";
		workspace.WriteFile(
			$".poly/luau/{LuauModuleMapService.MapFileName}",
			content.Replace("\r\n", "\n", StringComparison.Ordinal));
	}

	private static Task RestartAsync(
		LuauLspTestWorkspace workspace,
		string modulePath,
		string moduleSource,
		string serverPath,
		string serverSource,
		CancellationToken cancellationToken)
	{
		return workspace.RestartAsync(
			[
				(modulePath, moduleSource),
				(serverPath, serverSource)
			],
			cancellationToken);
	}

	private static Task<HashSet<string>> WaitForMathUtilAsync(
		LuauLspTestWorkspace workspace,
		string serverPath,
		Func<HashSet<string>, bool> condition,
		CancellationToken cancellationToken)
	{
		return workspace.WaitForCompletionsAsync(
			serverPath,
			1,
			"MathUtil.".Length,
			condition,
			cancellationToken);
	}
}
