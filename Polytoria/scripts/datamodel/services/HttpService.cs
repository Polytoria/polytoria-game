// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Http"), ExplorerExclude]
[SaveIgnore]
public sealed partial class HttpService : Instance
{
	private const int MaxRequestsPerMinute = 90;
	private int _requestsThisMinute = 0;
	private int _currentMinute;
	private PTHttpClient _client = new();

	public override void Init()
	{
		_client = new PTHttpClient();
		base.Init();
	}

	private bool RateLimit()
	{
		int minute = (int)DateTimeOffset.Now.ToUnixTimeSeconds() / 60;

		if (minute != _currentMinute)
		{
			_currentMinute = minute;
			_requestsThisMinute = 0;
		}

		if (_requestsThisMinute >= MaxRequestsPerMinute)
		{
			return false;
		}

		_requestsThisMinute++;
		return true;
	}

	private bool LegacyRateLimit(PTCallback? callback = null)
	{
		if (!Root.Network.IsServer)
		{
			callback?.Invoke(null, true, "Cannot call Http functions from client");
		}

		bool ratelimit = RateLimit();

		if (!ratelimit)
		{
			callback?.Invoke(null, true, "Http limit exceeded");
			return false;
		}
		return true;
	}

	[ScriptMethod]
	public async Task<HttpResponseData> RequestAsync(HttpRequestData data)
	{
		ServerGuard();

		if (data.URL == "")
		{
			throw new InvalidOperationException("URL is required");
		}

		if (!RateLimit())
		{
			throw new Exception("Http limit exceeded");
		}

		CheckURLPass(data.URL, Root.IsLocalTest);

		HttpContent? content = null;
		if (data.Body != null)
		{
			content = new StringContent(data.Body);
		}

		HttpMethod method = data.Method switch
		{
			HttpRequestData.HttpRequestMethodEnum.Get => HttpMethod.Get,
			HttpRequestData.HttpRequestMethodEnum.Post => HttpMethod.Post,
			HttpRequestData.HttpRequestMethodEnum.Put => HttpMethod.Put,
			HttpRequestData.HttpRequestMethodEnum.Patch => HttpMethod.Patch,
			HttpRequestData.HttpRequestMethodEnum.Delete => HttpMethod.Delete,
			_ => throw new InvalidOperationException("Method not supported")
		};

		HttpRequestMessage msg = new()
		{
			Method = method,
			RequestUri = new(data.URL),
			Content = content,
		};

		if (data.Headers != null)
		{
			foreach ((string key, string val) in data.Headers)
			{
				if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
				{
					msg.Content?.Headers.ContentType = new MediaTypeHeaderValue(val);
				}
				else
				{
					msg.Headers.Add(key, val);
				}
			}
		}
		msg.Headers.Add("PT-World-ID", Root.WorldID.ToString());

		using HttpResponseMessage res = await _client.SendAsync(msg);
		Dictionary<string, string> headers = [];

		foreach ((string key, IEnumerable<string> val) in res.Headers)
		{
			headers[key] = string.Join(",", val);
		}

		HttpResponseData resData = new()
		{
			Success = res.IsSuccessStatusCode,
			StatusCode = (int)res.StatusCode,
			Body = await res.Content.ReadAsStringAsync(),
			Buffer = await res.Content.ReadAsByteArrayAsync(),
			Headers = headers,
			responseMsg = res
		};

		return resData;
	}

	public static void CheckURLPass(string url, bool isLocalTest)
	{
		// Check valid URL
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri))
		{
			throw new InvalidOperationException("Invalid URL");
		}

		// Block non-HTTP(S) schemes
		if (parsedUri.Scheme != Uri.UriSchemeHttps && parsedUri.Scheme != Uri.UriSchemeHttp)
		{
			throw new InvalidOperationException("Invalid URL scheme");
		}

		if (!isLocalTest)
		{
			if (parsedUri.Scheme != Uri.UriSchemeHttps)
			{
				throw new InvalidOperationException("Only HTTPs is allowed in production");
			}

			// Block localhost/loopback
			string host = parsedUri.Host.ToLowerInvariant();

			if (host == "localhost" || host == "loopback")
			{
				throw new InvalidOperationException("Access to localhost is not allowed in production");
			}

			// Block raw IP addresses
			if (IPAddress.TryParse(host, out _))
			{
				throw new InvalidOperationException("Access to raw IP addresses is not allowed in production");
			}

			var addresses = Dns.GetHostAddresses(host);
			if (addresses.Any(ip => ip.IsPrivate()))
			{
				throw new InvalidOperationException("Access to private IP addresses is not allowed in production");
			}
		}
	}

	[ScriptMethod]
	public async Task<string> GetAsync(string url, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Method = HttpRequestData.HttpRequestMethodEnum.Get, Headers = headers });
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PostAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Post, Headers = headers });
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PutAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Put, Headers = headers });
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> DeleteAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Delete, Headers = headers });
		return response.Body;
	}

	[ScriptMethod]
	public async Task<string> PatchAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Patch, Headers = headers });
		return response.Body;
	}

	[ScriptMethod]
	public async Task<byte[]> GetBufferAsync(string url, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Method = HttpRequestData.HttpRequestMethodEnum.Get, Headers = headers });
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PostBufferAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Post, Headers = headers });
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PutBufferAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Put, Headers = headers });
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> DeleteBufferAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Delete, Headers = headers });
		return response.Buffer;
	}

	[ScriptMethod]
	public async Task<byte[]> PatchBufferAsync(string url, string body, Dictionary<string, string>? headers = null)
	{
		HttpResponseData response = await RequestAsync(new() { URL = url, Body = body, Method = HttpRequestData.HttpRequestMethodEnum.Patch, Headers = headers });
		return response.Buffer;
	}


	[ScriptMethod, Attributes.Obsolete("Use GetAsync instead")]
	public void Get(string url, PTCallback? callback = null, Dictionary<string, string>? headers = null)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Get, url, null, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PostAsync instead")]
	public void Post(string url, string body, PTCallback? callback = null, Dictionary<string, string>? headers = null)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Post, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PutAsync instead")]
	public void Put(string url, string body, PTCallback? callback = null, Dictionary<string, string>? headers = null)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Put, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use DeleteAsync instead")]
	public void Delete(string url, string body, PTCallback? callback = null, Dictionary<string, string>? headers = null)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Delete, url, body, callback, headers);
		}
	}

	[ScriptMethod, Attributes.Obsolete("Use PatchAsync instead")]
	public void Patch(string url, string body, PTCallback? callback = null, Dictionary<string, string>? headers = null)
	{
		ServerGuard();
		if (LegacyRateLimit(callback))
		{
			DoLegacyRequest(HttpMethod.Patch, url, body, callback, headers);
		}
	}

	private async void DoLegacyRequest(HttpMethod method, string url, string? body, PTCallback? callback, Dictionary<string, string>? headers)
	{
		try
		{
			CheckURLPass(url, Root.IsLocalTest);
		}
		catch (Exception ex)
		{
			callback?.Invoke(null, false, ex.Message);
			return;
		}

		HttpContent? content = null;
		if (body != null)
		{
			content = new StringContent(body);
		}

		HttpRequestMessage msg = new()
		{
			Method = method,
			RequestUri = new(url),
			Content = content,
		};

		if (headers != null)
		{
			foreach ((string key, string val) in headers)
			{
				if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
				{
					msg.Content?.Headers.ContentType = new MediaTypeHeaderValue(val);
				}
				else
				{
					msg.Headers.Add(key, val);
				}
			}
		}
		msg.Headers.Add("PT-Game-ID", Root.WorldID.ToString());

		try
		{
			using HttpResponseMessage res = await _client.SendAsync(msg);

			res.EnsureSuccessStatusCode();

			string resContent = await res.Content.ReadAsStringAsync();
			callback?.Invoke(resContent, false, "");
		}
		catch (Exception ex)
		{
			callback?.Invoke(null, true, ex.Message);
		}
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Http can only be accessed by server");
	}
}
