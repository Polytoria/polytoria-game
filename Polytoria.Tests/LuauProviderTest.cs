// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Scripting.Luau;

namespace Polytoria.Tests;

public sealed class LuauProviderTest
{
	[Fact]
	public void Test_ProviderIsolation()
	{
		using LuauProvider first = new();
		using LuauProvider second = new();

		Assert.True(first.GlobalLuaState.IsAlive);
		Assert.True(second.GlobalLuaState.IsAlive);

		first.Dispose();

		Assert.False(first.GlobalLuaState.IsAlive);
		Assert.True(second.GlobalLuaState.IsAlive);
		Assert.NotEmpty(second.CompileSource("return 42"));
	}

	[Fact]
	public void Test_UnrefAfterDispose()
	{
		using LuauProvider provider = new();
		LuaState state = provider.GlobalLuaState;

		state.PushString("kept alive");
		int reference = state.Ref();
		provider.Dispose();

		Assert.False(state.TryUnref(reference));
	}
}
