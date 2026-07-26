// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.DocsGen;
using static Polytoria.DocsGen.APIReferenceGenerator;

namespace Polytoria.Tests;

public class LuaDefinitionGeneratorTest
{
	[Fact]
	public void ClassesUseCurrentLuauExternTypeSyntax()
	{
		ScriptClass vectorType = new()
		{
			Name = "Vector3",
			BaseType = null,
			Properties = [],
			Methods = [],
			Events = [],
			IsStatic = false,
			IsAbstract = false,
			IsInstantiable = false,
			StaticAlias = null
		};

		string definition = LuaDefinitionGenerator.GenerateClass(vectorType);

		Assert.StartsWith("declare extern type Vector3 with", definition);
		Assert.DoesNotContain("declare class", definition);
	}
}
