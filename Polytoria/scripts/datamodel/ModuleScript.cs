// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting.Luau;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ModuleScript : Script
{
	internal int? CachedLuauResultRef { get; set; } = null;

	private bool _singleton = false;
	private bool _singletonRequired = false;

	[Editable, ScriptProperty]
	public bool Singleton
	{
		get => _singleton;
		set
		{
			_singleton = value;
			OnPropertyChanged();
			TryRunSingleton();
		}
	}

	public override void EnterTree()
	{
		CheckSource();
		base.EnterTree();
	}

	public override void Ready()
	{
		TryRunSingleton();
		base.Ready();
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

	internal void NotifyBytecodeReceived()
	{
		TryRunSingleton();
	}

	private void TryRunSingleton()
	{
		if (!_singleton || _singletonRequired) return;
		if (Root == null || IsDeleted) return;
		if (Root.SessionType == World.SessionTypeEnum.Creator) return;
		if (!IsEnabled || IsHidden) return;
		if (!IsNetworkReady) return;

		if (!Root.IsLoaded)
		{
			Root.Loaded.Once(TryRunSingleton);
			return;
		}

		_singletonRequired = true;
		LuauProvider.Singleton.RequireSingleton(this);
	}

	private void RequestSource()
	{
		Root.Network.ScriptSync.RequestSource(this);
	}
}
