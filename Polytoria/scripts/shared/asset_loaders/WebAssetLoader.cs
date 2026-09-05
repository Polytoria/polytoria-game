// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Shared.AssetLoaders;

public partial class WebAssetLoader : Node
{
	public WebAssetLoader()
	{
		Singleton = this;
	}

	public static WebAssetLoader Singleton { get; private set; } = null!;

	private const int MAX_CONCURRENT_REQUESTS = 4;

	private readonly PTHttpClient _client = new();

	private readonly SemaphoreSlim _downloadSlots = new(MAX_CONCURRENT_REQUESTS);

	private readonly ConcurrentDictionary<WebCacheItem, WebCacheItem> _cache = [];

	private readonly ConcurrentDictionary<WebCacheItem, Lazy<Task<WebCacheItem>>> _pendingRequests = [];

	private async Task<WebCacheItem> LoadItem(WebCacheItem item)
	{
		try
		{
			await _downloadSlots.WaitAsync();
			try
			{
				return await LoadResource(item);
			}
			finally
			{
				_downloadSlots.Release();
			}
		}
		finally
		{
			_pendingRequests.TryRemove(item, out _);
		}
	}

	private async Task<WebCacheItem> LoadResource(WebCacheItem item)
	{
		if (string.IsNullOrEmpty(item.URL))
		{
			return new WebCacheItem();
		}

		byte[] buffer = await _client.GetByteArrayAsync(item.URL);

		switch (item.Type)
		{
			case WebResourceType.Image:
				{
					Image image = await Task.Run(() => DecodeImage(buffer, item.URL));
					item.Resource = ImageTexture.CreateFromImage(image);

					_cache.TryAdd(item, item);
					return item;
				}
			default:
				throw new NotImplementedException($"Resource type {item.Type} not implemented!");
		}
	}

	private static Image DecodeImage(byte[] buffer, string url)
	{
		Image image = new();
		if (url.EndsWith(".png"))
		{
			image.LoadPngFromBuffer(buffer);
		}
		else if (url.EndsWith(".jpg"))
		{
			image.LoadJpgFromBuffer(buffer);
		}
		else
		{
			image.LoadPngFromBuffer(buffer);
		}

		image.GenerateMipmaps();
		return image;
	}

	public void GetResource(WebCacheItem item, Action<Resource> callback)
	{
		if (_cache.TryGetValue(item, out WebCacheItem cached))
		{
			Callable.From(() => callback(cached.Resource)).CallDeferred();
			return;
		}

		Lazy<Task<WebCacheItem>> task = _pendingRequests.GetOrAdd(item, _ => new Lazy<Task<WebCacheItem>>(() => LoadItem(item), LazyThreadSafetyMode.ExecutionAndPublication));

		_ = WaitForResource(task.Value, item, callback);
	}

	private static async Task WaitForResource(Task<WebCacheItem> task, WebCacheItem item, Action<Resource> callback)
	{
		try
		{
			WebCacheItem result = await task;
			Callable.From(() => callback(result.Resource)).CallDeferred();
		}
		catch (Exception exception)
		{
			PT.PrintErr($"Failed to load resource (Type: {item.Type}, URL: {item.URL}): {exception.Message}");
		}
	}
}

public enum WebResourceType
{
	Image
}

public struct WebCacheItem
{
	public string URL { get; set; }
	public WebResourceType Type { get; set; }
	public Resource Resource { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is WebCacheItem item && item.Type == Type && item.URL == URL;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, URL);
	}

	public static bool operator ==(WebCacheItem left, WebCacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(WebCacheItem left, WebCacheItem right)
	{
		return !(left == right);
	}
}
