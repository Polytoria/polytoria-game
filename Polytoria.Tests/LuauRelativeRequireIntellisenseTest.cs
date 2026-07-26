// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.LSP;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class LuauRelativeRequireIntellisenseTest
{
	[Fact]
	public async Task ScriptParentRequireResolvesModuleExports()
	{
		CancellationToken testCancellation = TestContext.Current.CancellationToken;
		using LuauLspTestWorkspace workspace = new("polytoria-relative-require-lsp");

		string moduleSource = """
			local MathUtil = {}
			MathUtil.Version = "2.0"

			function MathUtil.clamp(value, minimum, maximum)
				return math.max(minimum, math.min(maximum, value))
			end

			return MathUtil
			""";
		string modulePath = workspace.WriteFile("scripts/modules/MathUtil.luau", moduleSource);

		string serverSource = """
			local MathUtil = require(script.Parent.Parent.Modules.MathUtil)
			MathUtil.
			""";
		string serverPath = workspace.WriteFile("scripts/server/controllers/Test.server.luau", serverSource);

		string mapContent = """
			# Polytoria Luau module map v2
			# S	world-id	source-file	source-world-path
			# M	world-id	module-world-path	module-file
			S	0	scripts/server/controllers/Test.server.luau	world.ScriptService.Controllers.Test
			M	0	world.ScriptService.Modules.MathUtil	scripts/modules/MathUtil.luau
			""";
		workspace.WriteFile(
			$".poly/luau/{LuauModuleMapService.MapFileName}",
			mapContent.Replace("\r\n", "\n", StringComparison.Ordinal));

		await workspace.RestartAsync(
			[
				(modulePath, moduleSource),
				(serverPath, serverSource)
			],
			testCancellation);

		HashSet<string> labels = await workspace.WaitForCompletionsAsync(
			serverPath,
			1,
			"MathUtil.".Length,
			labels => labels.Contains("Version") && labels.Contains("clamp"),
			testCancellation);

		Assert.Contains("Version", labels);
		Assert.Contains("clamp", labels);
	}
}
