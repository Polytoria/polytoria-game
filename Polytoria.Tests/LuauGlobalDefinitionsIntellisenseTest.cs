// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class LuauGlobalDefinitionsIntellisenseTest
{
	[Fact]
	public async Task WorkspaceConfigurationKeepsPolytoriaGlobalsLoaded()
	{
		CancellationToken testCancellation = TestContext.Current.CancellationToken;
		using LuauLspTestWorkspace workspace = new("polytoria-global-definitions-lsp");

		workspace.WriteFile(".poly/luau/def.d.luau", """
			declare extern type Instance with
				Name: string
			end

			declare extern type Vector with
				X: number
				Y: number
				Z: number
			end

			declare Vector: {
				New: (number, number, number) -> Vector,
			}

			declare extern type Vector3 with
				X: number
				Y: number
				Z: number
			end

			declare Vector3: {
				New: (number, number, number) -> Vector3,
			}
			""");

		string scriptSource = """
			local selected: Inst
			local direction = Vec
			""";
		string scriptPath = workspace.WriteFile("scripts/server/Globals.server.luau", scriptSource);

		await workspace.RestartAsync(
			[
				(scriptPath, scriptSource)
			],
			testCancellation);

		HashSet<string> typeLabels = await workspace.WaitForCompletionsAsync(
			scriptPath,
			0,
			"local selected: Inst".Length,
			labels => labels.Contains("Instance"),
			testCancellation);

		HashSet<string> valueLabels = await workspace.WaitForCompletionsAsync(
			scriptPath,
			1,
			"local direction = Vec".Length,
			labels => labels.Contains("Vector") && labels.Contains("Vector3"),
			testCancellation);

		Assert.Contains("Instance", typeLabels);
		Assert.Contains("Vector", valueLabels);
		Assert.Contains("Vector3", valueLabels);
	}
}
