// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Abstract]
public partial class Script : Instance
{
	public bool Ran = false;

	internal Scripting.Luau.LuaState? LuauState;
	internal Scripting.Luau.LuaState? LuauMainThread;

	[CloneInclude]
	public byte[]? Bytecode { get; internal set; }

	internal readonly Dictionary<object, int> LuauUserdataCache = [];
	internal readonly HashSet<Scripting.Luau.LuaObject> LuauObjectCache = [];
	internal readonly HashSet<IntPtr> LuauFunctionPointers = [];

	private string? _source;
	private FileLinkAsset? _linkedFile;
	private bool _compatibility = false;
	private bool _isEnabled = true;

	[Editable(IsHidden = true), NoSync, CloneInclude, SaveIgnore]
	public string Source
	{
		get
		{
			if (_linkedFile != null)
			{
				byte[]? data = _linkedFile.ReadFile();
				if (data != null)
				{
					return data.GetStringFromUtf8();
				}
			}

			return _source ?? "";
		}
		set
		{
			_source = value;
		}
	}

	[Editable, ScriptProperty]
	public bool IsEnabled
	{
		get
		{
			return _isEnabled;
		}
		set
		{
			_isEnabled = value;
			if (_isEnabled)
			{
				TryRun();
			}
			else
			{
				Stop();
			}
		}
	}

	[Editable, NoSync, CloneInclude, SaveInclude]
	public FileLinkAsset? LinkedScript
	{
		get => _linkedFile;
		set
		{
			if (_linkedFile != null && _linkedFile != value)
			{
				_linkedFile.UnlinkFrom(this);
			}
			_linkedFile = value;
			_linkedFile?.LinkTo(this);
		}
	}

	[Editable]
	public bool Compatibility
	{
		get => _compatibility;
		set
		{
			_compatibility = value;
		}
	}

	/// <summary>
	/// Determine if this script should execute
	/// </summary>
	internal bool ShouldContinue => (this is ModuleScript || Ran) && IsEnabled && !IsDeleted;

	public IScriptLanguageProvider LanguageProvider = null!;

	public ScriptLanguagesEnum ChosenLanguage = ScriptLanguagesEnum.Luau;
	public ScriptPermissionFlags PermissionFlags = 0;

	public void TryRun()
	{
		if (this is ModuleScript) return;
		if (Root.SessionType != World.SessionTypeEnum.Client) return;
		if (Ran) return;
		if (IsHidden) return;
		if (!IsEnabled) return;
		if (Source == "" && Bytecode == null) return;
		if ((this is ServerScript && !Root.IsLoaded) || !IsNetworkReady) return;
		if (this is ClientScript && Root.Network.IsServer) return;
		if (this is ServerScript && !Root.Network.IsServer) return;
		Run();
	}

	public void Run()
	{
		Ran = true;
		Root.ScriptService.Run(this);
	}

	public void Stop()
	{
		if (!Ran) return;
		Root?.ScriptService?.Close(this);
	}

	public override void PreDelete()
	{
		Stop();
		base.PreDelete();
	}

	public override void Init()
	{
		SetProcess(true);
		SetPhysicsProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		if (Root.IsLoaded)
		{
			TryRun();
		}
		base.Ready();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (!ShouldContinue) return;
		if (LanguageProvider == null) return;

		LanguageProvider.CallUpdate(this, delta);
	}

	public override void PhysicsProcess(double delta)
	{
		base.PhysicsProcess(delta);
		if (!ShouldContinue) return;
		if (LanguageProvider == null) return;

		LanguageProvider.CallFixedUpdate(this, delta);
	}

	public override void HiddenChanged(bool to)
	{
		if (!to)
		{
			TryRun();
		}
		base.HiddenChanged(to);
	}

	[ScriptMethod]
	public void Call(string funcName, params object?[]? args)
	{
		try
		{
			CallAsync(funcName, args);
		}
		catch
		{
			throw;
		}
	}

	[ScriptMethod]
	public async void CallAsync(string funcName, params object?[]? args)
	{
		await LanguageProvider.CallAsync(this, funcName, args);
	}

	internal string CreateLuaFileName()
	{
		if (this is ServerScript)
		{
			return $"{Name}.server.luau";
		}
		else if (this is ClientScript)
		{
			return $"{Name}.client.luau";
		}
		else
		{
			return $"{Name}.luau";
		}
	}

	[ScriptMethod(Permissions = ScriptPermissionFlags.IOWrite)]
	public void LinkWithScriptFile(string scriptPath)
	{
		LinkedScript = Root.Assets.GetFileLinkByPath(scriptPath);
	}

	public void TryCompile()
	{
		if (Bytecode != null) return;
		Root.ScriptService.CompileScript(this);
	}
}
