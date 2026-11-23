// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Datamodel.Services;
using Polytoria.Providers.Datastore;
using Polytoria.Scripting;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Data;

public partial class Datastore : IScriptObject
{
	private string _dsKey = null!;
	public DatastoreService DatastoreService { get; set; } = null!;

	public IDatastoreProvider Provider { get; set; } = null!;

	[ScriptLegacyProperty("Loading")] public bool LegacyLoading { get; private set; } = true;

	[ScriptLegacyProperty("Loaded")] public PTSignal LegacyLoaded { get; private set; } = new();

	[ScriptProperty]
	public string Key => _dsKey;

	public void Connect(string key, IDatastoreProvider provider)
	{
		_dsKey = key;
		Provider = provider;
		Provider.Connect(key, this);
		LegacyLoading = false;
		LegacyLoaded.Invoke();
	}

	[ScriptMethod]
	public async Task<object?> GetAsync(string key)
	{
		return await Provider.ReadData(key);
	}

	[ScriptMethod]
	public async Task SetAsync(string key, object value)
	{
		await Provider.WriteData(key, value);
	}

	[ScriptMethod]
	public async Task RemoveAsync(string key)
	{
		await Provider.WriteData(key, null);
	}

	[ScriptMethod, ScriptLegacyMethod(nameof(Get))]
	public void Get(string key, PTCallback? callback)
	{
		_ = GetAsync(key).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				object? val = tsk.Result;
				callback?.Invoke(val, true, null);
			}
			else
			{
				callback?.Invoke(null, false, tsk.Exception?.Message);
			}
		});
	}

	[ScriptMethod, ScriptLegacyMethod(nameof(Set))]
	public void Set(string key, object value, PTCallback? callback)
	{
		_ = SetAsync(key, value).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				callback?.Invoke(true);
			}
			else
			{
				callback?.Invoke(false, tsk.Exception?.Message);
			}
		});
	}

	[ScriptMethod, ScriptLegacyMethod(nameof(Remove))]
	public void Remove(string key, PTCallback? callback)
	{
		_ = RemoveAsync(key).ContinueWith(tsk =>
		{
			if (tsk.IsCompletedSuccessfully)
			{
				callback?.Invoke(true);
			}
			else
			{
				callback?.Invoke(false, tsk.Exception?.Message);
			}
		});
	}

	[ScriptMethod]
	public void Disconnect()
	{
		Provider.Dispose();
		LegacyLoaded.DisconnectAll();
	}
}
