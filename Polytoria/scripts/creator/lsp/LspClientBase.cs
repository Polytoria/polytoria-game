using Polytoria.Creator.LSP.Schemas;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Creator.LSP;

public abstract class LspClientBase : IDisposable
{
	private readonly Stream _input;
	private readonly Stream _output;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = [];
	private int _requestId;
	private readonly Task? _readerTask;
	private readonly CancellationTokenSource _cts = new();

	protected LspClientBase(Stream input, Stream output)
	{
		_input = input;
		_output = output;
		_readerTask = Task.Run(ReadMessagesAsync);
	}

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code 
#pragma warning disable IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling. 
	protected async Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
	{
		int id = Interlocked.Increment(ref _requestId);
		TaskCompletionSource<JsonElement> tcs = new();
		_pendingRequests[id] = tcs;

		try
		{
			LspRequest request = new() { Id = id, Method = method, Params = parameters };
			await WriteMessageAsync(request, cancellationToken);

			using CancellationTokenSource combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
			JsonElement result = await tcs.Task.WaitAsync(combined.Token);

			return JsonSerializer.Deserialize<T>(result.GetRawText(), LspJsonContext.Default.Options);
		}
		finally
		{
			_pendingRequests.Remove(id);
		}
	}

	protected Task SendNotificationAsync(string method, object? parameters = null)
	{
		LspNotification notification = new() { Method = method, Params = parameters };
		return WriteMessageAsync(notification, CancellationToken.None);
	}

	protected async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
	{
		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			string json = JsonSerializer.Serialize(message, LspJsonContext.Default.Options);
			byte[] content = Encoding.UTF8.GetBytes(json);
			byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {content.Length}\r\n\r\n");

			await _output.WriteAsync(header, cancellationToken);
			await _output.WriteAsync(content, cancellationToken);
			await _output.FlushAsync(cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}
	}
#pragma warning restore IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling. 
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code 

	private async Task ReadMessagesAsync()
	{
		byte[] headerBuffer = new byte[1024];
		byte[] contentBuffer = new byte[65536];

		try
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				int contentLength = await ReadHeaderAsync(headerBuffer);
				if (contentLength <= 0) break;

				if (contentLength > contentBuffer.Length)
					contentBuffer = new byte[contentLength];

				int bytesRead = 0;
				while (bytesRead < contentLength)
				{
					int read = await _input.ReadAsync(contentBuffer.AsMemory(bytesRead, contentLength - bytesRead), _cts.Token);
					if (read == 0) return;
					bytesRead += read;
				}

				string json = Encoding.UTF8.GetString(contentBuffer, 0, contentLength);
				ProcessMessage(json);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"LSP Reader error: {ex.Message}");
		}
	}

	private async Task<int> ReadHeaderAsync(byte[] buffer)
	{
		int pos = 0;
		int contentLength = -1;

		while (true)
		{
			int b = _input.ReadByte();
			if (b == -1) return -1;

			buffer[pos++] = (byte)b;

			if (pos >= 4 && buffer[pos - 4] == '\r' && buffer[pos - 3] == '\n' && buffer[pos - 2] == '\r' && buffer[pos - 1] == '\n')
			{
				string headerText = Encoding.ASCII.GetString(buffer, 0, pos - 4);
				foreach (string line in headerText.Split('\n'))
				{
					if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
					{
						contentLength = int.Parse(line[15..].Trim());
					}
				}
				return contentLength;
			}
		}
	}

	private void ProcessMessage(string json)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			// request from the server to the client?
			if (root.TryGetProperty("method", out JsonElement methodReqProp) &&
				root.TryGetProperty("id", out JsonElement serverRequestId))
			{
				HandleServerRequest(methodReqProp.GetString() ?? "", serverRequestId);
				return;
			}

			// response to a request we made?
			if (root.TryGetProperty("id", out JsonElement idProp) && idProp.ValueKind == JsonValueKind.Number)
			{
				int id = idProp.GetInt32();
				if (_pendingRequests.TryGetValue(id, out var tcs))
				{
					if (root.TryGetProperty("result", out JsonElement result))
					{
						tcs.SetResult(result.Clone());
					}
					else if (root.TryGetProperty("error", out JsonElement error))
					{
						tcs.SetException(new Exception($"LSP Error: {error}"));
					}
				}
			}
			// notification from the server?
			else if (root.TryGetProperty("method", out JsonElement methodNoti) && methodNoti.ValueKind == JsonValueKind.String)
			{
				if (root.TryGetProperty("params", out JsonElement param))
				{
					HandleServerNotification(methodNoti.GetString() ?? "", param);
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error processing LSP message: {ex.Message}");
		}
	}

	// Virtual hooks that derived lsp clients can override
	protected virtual void HandleServerNotification(string method, JsonElement param) { }
	protected virtual void HandleServerRequest(string method, JsonElement id) { }

	public virtual void Dispose()
	{
		_cts.Cancel();
		_readerTask?.Wait(TimeSpan.FromSeconds(1));
		_cts.Dispose();
		_writeLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
