// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
#if !USE_NATIVE_HTTP
using System;
using System.Net;
#endif

namespace Polytoria.Shared;

public partial class PTHttpClient
{
	private const int DefaultDownloadChunkSize = 65536;
#if USE_NATIVE_HTTP
	private static readonly HttpClient _httpClient = new(
		new HttpClientHandler
		{
			AllowAutoRedirect = false
		}
	);
#endif
	public Dictionary<string, string> DefaultRequestHeaders { get; set; } = [];

	public PTHttpClient()
	{
		DefaultRequestHeaders["User-Agent"] = $"Polytoria Client {Globals.AppVersion}";
	}

#if !USE_NATIVE_HTTP
	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		// Check nohttp feature flag
		if (Globals.UseNoHttp) throw new HttpRequestException("Http is disabled via feature flag");

		List<string> headers = [];

		foreach ((string k, string v) in DefaultRequestHeaders)
		{
			headers.Add(k + ": " + v);
		}

		foreach (var item in msg.Headers)
		{
			headers.Add(item.Key + ": " + string.Join(", ", item.Value));
		}

		// Add content headers if present
		if (msg.Content != null)
		{
			foreach (var item in msg.Content.Headers)
			{
				headers.Add(item.Key + ": " + string.Join(", ", item.Value));
			}
		}

		TaskCompletionSource<HttpResponseMessage> tcs = new();

		// needs to be callable due to add_child
		Callable.From(() =>
		{
			// Workaround since callable dont support async
			async void a()
			{
				byte[] body = msg.Content != null ? await msg.Content.ReadAsByteArrayAsync() : [];

				HttpRequest req = new()
				{
					DownloadChunkSize = DefaultDownloadChunkSize,
					MaxRedirects = 0
				};

				Globals.Singleton.AddChild(req);

				req.RequestCompleted += (result, responseCode, responseHeaders, responseBody) =>
				{
					HttpResponseMessage response = new((HttpStatusCode)responseCode)
					{
						Content = new ByteArrayContent(responseBody)
					};

					foreach (string header in responseHeaders)
					{
						string[] parts = header.Split(':', 2);
						if (parts.Length == 2)
						{
							response.Headers.TryAddWithoutValidation(parts[0].Trim(), parts[1].Trim());
						}
					}

					req.QueueFree();
					tcs.SetResult(response);
				};

				Error error = req.RequestRaw(
					msg.RequestUri?.ToString() ?? throw new InvalidOperationException("URL is null"),
					[.. headers],
					Enum.Parse<Godot.HttpClient.Method>(msg.Method.Method.ToLower().Capitalize()),
					new ReadOnlySpan<byte>(body)
				);

				if (error != Error.Ok)
				{
					throw new HttpRequestException($"HttpRequest failed with error: {error}");
				}
			}

			a();
		}).CallDeferred();

		return tcs.Task;
	}
#else
	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		foreach ((string key, string val) in DefaultRequestHeaders)
		{
			msg.Headers.TryAddWithoutValidation(key, val);
		}
		return _httpClient.SendAsync(msg);
	}
#endif

	public async Task<HttpResponseMessage> GetAsync(string url)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		return await SendAsync(msg);
	}

	public async Task<T?> GetFromJsonAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		msg.Headers.TryAddWithoutValidation("Accept", "application/json");

		using HttpResponseMessage response = await SendAsync(msg);
		response.EnsureSuccessStatusCode();

		string json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize(json, jsonTypeInfo);
	}

	public async Task<byte[]> GetByteArrayAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsByteArrayAsync();
	}

	public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
	{
		using HttpRequestMessage msg = new(HttpMethod.Post, url)
		{
			Content = content
		};

		return await SendAsync(msg);
	}

	public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string url, T value, JsonTypeInfo<T> jsonTypeInfo)
	{
		string json = JsonSerializer.Serialize(value, jsonTypeInfo);

		using HttpRequestMessage msg = new(HttpMethod.Post, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};

		return await SendAsync(msg);
	}

	public async Task<string> GetStringAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsStringAsync();
	}
}
