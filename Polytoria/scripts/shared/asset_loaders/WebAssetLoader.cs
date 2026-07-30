// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Polytoria.Shared;

namespace Polytoria.Shared.AssetLoaders;

public partial class WebAssetLoader : Node
{
	public WebAssetLoader()
	{
		Singleton = this;
		Task.Run(Process);
	}

	public static WebAssetLoader Singleton { get; private set; } = null!;

	private const int MAX_CONCURRENT_REQUESTS = 1;

	private readonly PTHttpClient _client = new();

	private readonly BlockingCollection<(WebCacheItem Item, Action<Resource> Callback)> _queue = [];

	private readonly ConcurrentDictionary<WebCacheItem, WebCacheItem> _cache = [];

	private async Task Process()
	{
		List<Task> workers = [];
		for (int i = 0; i < MAX_CONCURRENT_REQUESTS; i++)
		{
			workers.Add(Task.Run(async () =>
			{
				while (true)
				{
					(WebCacheItem item, Action<Resource> callback) = _queue.Take();

					try
					{
						if (!_cache.TryGetValue(item, out WebCacheItem result))
						{
							result = await LoadResource(item);
						}

						Callable.From(() =>
						{
							callback(result.Resource);
						}).CallDeferred();
					}
					catch (Exception exception)
					{
						if (!Globals.IsInGDEditor)
						{
							PT.PrintErr("Failed to load resource (Type: " + item.Type + ", URL: " + item.URL + "): " + exception.Message);
						}
					}
				}
			}));
		}

		await Task.WhenAll(workers);
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
					Image image = new();
					if (item.URL.EndsWith(".png"))
					{
						image.LoadPngFromBuffer(buffer);
					}
					else if (item.URL.EndsWith(".jpg"))
					{
						image.LoadJpgFromBuffer(buffer);
					}
					else
					{
						image.LoadPngFromBuffer(buffer);
					}

					image.GenerateMipmaps();
					item.Resource = ImageTexture.CreateFromImage(image);

					_cache.TryAdd(item, item);
					return item;
				}
			default:
				throw new NotImplementedException($"Resource type {item.Type} not implemented!");
		}
	}

	public void GetResource(WebCacheItem item, Action<Resource> callback)
	{
		_queue.Add((item, callback));
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
		return URL.GetHashCode();
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
