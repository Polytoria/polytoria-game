// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Extensions;

public static class RuntimeExtensions
{
	private static readonly RuntimeExtensionHost _host = new();
	public static void Register(IRuntimeExtension extension) => _host.Register(extension);
	internal static void Initialize() => _host.Initialize();
	internal static void Shutdown() => _host.Shutdown();
}

internal class RuntimeExtensionHost
{
	private readonly List<IRuntimeExtension> _registered = [];
	private readonly List<IRuntimeExtension> _initialized = [];
	private bool _started;

	public void Register(IRuntimeExtension extension)
	{
		ArgumentNullException.ThrowIfNull(extension);

		if (_started)
			throw new InvalidOperationException("Cannot register runtime extension after initialize");

		_registered.Add(extension);
	}

	public void Initialize()
	{
		if (_started) return;
		_started = true;

		foreach (IRuntimeExtension extension in _registered)
		{
			try
			{
				extension.Initialize();
				_initialized.Add(extension);
				PT.Print("Runtime extension initialized: ", extension.GetType().Name);
			}
			catch (Exception ex)
			{
				PT.PrintErr("Failed to initialize runtime extension ", extension.GetType().Name, ": ", ex);
			}
		}
	}

	public void Shutdown()
	{
		for (int i = _initialized.Count - 1; i >= 0; i--)
		{
			IRuntimeExtension extension = _initialized[i];
			try
			{
				extension.Shutdown();
			}
			catch (Exception ex)
			{
				PT.PrintErr("Failed to shutdown runtime extension ", extension.GetType().Name, ": ", ex);
			}
		}

		_initialized.Clear();
	}
}
