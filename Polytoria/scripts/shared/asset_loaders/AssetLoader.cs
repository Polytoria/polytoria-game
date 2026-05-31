// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Providers.AssetLoaders;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Shared.AssetLoaders;

public partial class AssetLoader : Node
{

	private readonly record struct AssetCacheKey(ResourceType Type, uint ID, Vector2I? Resize);
	private const int DefaultMaxConcurrentRequests = 5;

	public AssetLoader()
	{
		Singleton = this;
		AssetProvider = new PTAssetProvider();
	}

	public static AssetLoader Singleton { get; private set; } = null!;
	public bool UseAssetLoader { get; set; } = true;

	private long _assetSizeBytes = 0;
	internal long AssetSizeBytes => _assetSizeBytes;
	internal int PendingAssetsCount => _pendingRequests.Count;
	internal int AssetCacheCount => _cache.Count;

	private readonly ConcurrentDictionary<AssetCacheKey, CacheItem> _cache = [];
	private readonly ConcurrentDictionary<AssetCacheKey, Lazy<Task<CacheItem>>> _pendingRequests = [];
	public int MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;

	private SemaphoreSlim _loadSlots = null!;

	public IAssetProvider AssetProvider = null!;

	private static AssetCacheKey KeyFor(CacheItem item)
	{
		return new AssetCacheKey(item.Type, item.ID, item.Resize);
	}

	private async Task<CacheItem> LoadResource(CacheItem item)
	{
		if (item.ID == 0)
		{
			return item;
		}

		if (!UseAssetLoader)
		{
			return item;
		}

		return await AssetProvider.LoadResource(item);
	}

	public void GetResource(CacheItem item, Action<Resource> callback)
	{
		GetRawCache(item, result =>
		{
			callback(result.Resource);
		});
	}

	private async Task<CacheItem> LoadItem(CacheItem item, AssetCacheKey key)
	{
		if (_loadSlots == null)
		{
			_loadSlots = new(MaxConcurrentRequests);
		}

		await _loadSlots.WaitAsync();
		try
		{
			CacheItem result = await LoadResource(item);
			_cache[key] = result;
			Interlocked.Add(ref _assetSizeBytes, result.SizeBytes);
			return result;
		}
		finally
		{
			_pendingRequests.TryRemove(key, out _);
			_loadSlots.Release();
		}
	}

	public void GetRawCache(CacheItem item, Action<CacheItem> callback)
	{
		AssetCacheKey key = KeyFor(item);

		// Return cached asset
		if (_cache.TryGetValue(key, out CacheItem cached))
		{
			Callable.From(() => callback(cached)).CallDeferred();
			return;
		}

		Lazy<Task<CacheItem>> task = _pendingRequests.GetOrAdd(key, _ => new Lazy<Task<CacheItem>>(() => LoadItem(item, key), LazyThreadSafetyMode.ExecutionAndPublication));

		_ = WaitForResource(task.Value, item, callback);
	}

	private static async Task WaitForResource(Task<CacheItem> task, CacheItem item, Action<CacheItem> callback)
	{
		try
		{
			CacheItem result = await task;
			Callable.From(() => callback(result)).CallDeferred();
		}
		catch (Exception exception)
		{
			Callable.From(() => PT.PrintErr("Failed to load resource (Type: " + item.Type + ", ID: " + item.ID + "): " + exception.Message)).CallDeferred();
		}
	}
}

public enum ResourceType
{
	Mesh,
	Decal,
	Audio,
	AssetThumbnail,
	PlaceThumbnail,
	PlaceIcon,
	UserThumbnail,
	UserHeadshot,
	GuildThumbnail,
	GuildBanner,
	Asset,
	Font
}


public struct CacheItem
{
	public ResourceType Type { get; set; }
	public uint ID { get; set; }
	public string DirectURL { get; set; }
	public Vector2I? Resize { get; set; }
	public Resource Resource { get; set; }
	public long SizeBytes { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is CacheItem item && item.Type == Type && item.ID == ID && item.Resize == Resize;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, ID, Resize);
	}

	public static bool operator ==(CacheItem left, CacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(CacheItem left, CacheItem right)
	{
		return !(left == right);
	}
}
