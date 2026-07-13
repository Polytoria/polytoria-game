// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System.Threading.Tasks;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ModuleScript : Script
{
	internal int? CachedLuauResultRef { get; set; } = null;
	internal TaskCompletionSource<bool>? LuauLoadCompletion { get; set; }
	internal string? LuauLoadError { get; set; }
	internal ModuleLoadState LuauLoadState { get; set; }
	internal ModuleScript? LuauWaitingFor { get; set; }
	internal string? LuauLoadedSource { get; set; }

	public override void EnterTree()
	{
		CheckSource();
		base.EnterTree();
	}

	internal void CheckSource()
	{
		if (!Root.Network.IsServer)
		{
			if (Source == "" && Root.IsLoaded)
			{
				RequestSource();
			}
		}
	}


	private void RequestSource()
	{
		Root.Network.ScriptSync.RequestSource(this);
	}
}

internal enum ModuleLoadState
{
	NotLoaded,
	Loading,
	Loaded,
}
