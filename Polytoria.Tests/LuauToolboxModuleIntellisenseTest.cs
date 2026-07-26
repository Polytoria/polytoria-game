// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class LuauToolboxModuleIntellisenseTest
{
	[Fact]
	public async Task ToolboxModuleExportsRemainAvailableWithNativeDefinitions()
	{
		CancellationToken testCancellation = TestContext.Current.CancellationToken;
		using LuauLspTestWorkspace workspace = new("polytoria-toolbox-lsp");

		workspace.WriteFile(".poly/luau/def.d.luau", """
			declare extern type Vector3 with
				x: number
				y: number
				z: number
				sqrMagnitude: number
			end

			declare Vector3: {
				New: (number, number, number) -> Vector3,
				Normalize: (Vector3) -> Vector3,
				Dot: (Vector3, Vector3) -> number,
				Cross: (Vector3, Vector3) -> Vector3,
			}
			""");

		// The module map intentionally does not exist yet. Toolbox files must still
		// expose their returned table while the native API definitions are active.
		string moduleSource = """
			local CFrame = {}
			CFrame.Version = "V1.22"
			CFrame.identity = {}

			local function normalize(value)
				return Vector3.Normalize(value)
			end

			function CFrame.New(value)
				return normalize(value)
			end

			function CFrame.lookAt(at, target)
				local direction = Vector3.New(target.x - at.x, target.y - at.y, target.z - at.z)
				return Vector3.Normalize(direction)
			end

			CFrame.fromPosition = function(value)
				return CFrame.New(value)
			end

			return CFrame
			""";
		string modulePath = workspace.WriteFile("toolbox/CFrameModule/CFrameModule.luau", moduleSource);

		string serverSource = """
			local CFrameUtil = require("../../toolbox/CFrameModule/CFrameModule")
			CFrameUtil.
			""";
		string serverPath = workspace.WriteFile("scripts/server/Test.server.luau", serverSource);

		await workspace.RestartAsync(
			[
				(modulePath, moduleSource),
				(serverPath, serverSource)
			],
			testCancellation);

		HashSet<string> labels = await workspace.WaitForCompletionsAsync(
			serverPath,
			1,
			"CFrameUtil.".Length,
			labels => labels.Contains("New") &&
				labels.Contains("Version") &&
				labels.Contains("identity") &&
				labels.Contains("lookAt") &&
				labels.Contains("fromPosition"),
			testCancellation);

		Assert.Contains("New", labels);
		Assert.Contains("Version", labels);
		Assert.Contains("identity", labels);
		Assert.Contains("lookAt", labels);
		Assert.Contains("fromPosition", labels);
	}
}
