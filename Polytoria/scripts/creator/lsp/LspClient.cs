// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
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

public class LspClient : IDisposable
{
	private readonly Stream _input;
	private readonly Stream _output;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = [];
	private int _requestId;
	private readonly Task? _readerTask;
	private readonly CancellationTokenSource _cts = new();

	public readonly Dictionary<string, string> LspPathToFull = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> FullToLspPath = new(StringComparer.OrdinalIgnoreCase);
	public event Action<LspPublishDiagnosticsParams>? PublishDiagnostics;

	public LspClient(Stream input, Stream output)
	{
		_input = input;
		_output = output;
		_readerTask = Task.Run(ReadMessagesAsync);
	}

	public async Task InitializeAsync(string workspacePath)
	{
		LspInitializeParams initParams = new()
		{
			RootUri = PathToUri(workspacePath),
			Capabilities = new()
			{
				TextDocument = new()
				{
					Completion = new()
					{
						CompletionItem = new() { SnippetSupport = false }
					},
					Hover = new()
					{
						ContentFormat = ["markdown"]
					},
					Synchronization = new()
					{
						DidSave = true,
						WillSave = true,
						WillSaveWaitUntil = true
					}
				},
				Workspace = new()
				{
					ApplyEdit = true,
					WorkspaceEdit = new() { DocumentChanges = true },
					Configuration = true,
					DidChangeWatchedFiles = new()
					{
						DynamicRegistration = true
					}
				},
				General = new()
				{
					PositionEncodings = ["utf-8"]
				}
			}
		};

		await SendRequestAsync<LspInitializeResult>("initialize", initParams);
		await SendNotificationAsync("initialized", new EmptyParams());
	}

	public Task DidOpenAsync(string path, string languageId, string text)
	{
		string p = PathToUri(path);
		LspPathToFull[p] = path;
		FullToLspPath[path] = p;
		return SendNotificationAsync("textDocument/didOpen", new LspDidOpenParams
		{
			TextDocument = new LspTextDocumentItem
			{
				Uri = p,
				LanguageId = languageId,
				Version = 1,
				Text = text
			}
		});
	}

	public Task DidCloseAsync(string path)
	{
		if (FullToLspPath.Remove(path, out string? p)) LspPathToFull.Remove(p);
		return SendNotificationAsync("textDocument/didClose", new LspDidCloseParams
		{
			TextDocument = new() { Uri = PathToUri(path) }
		});
	}

	public Task DidChangeAsync(string path, string text, int version)
	{
		return SendNotificationAsync("textDocument/didChange", new LspDidChangeParams
		{
			TextDocument = new()
			{
				Uri = PathToUri(path),
				Version = version
			},
			ContentChanges = [new() { Text = text }]
		});
	}

	public async Task<LspCompletionItem[]?> RequestCompletionAsync(string path, int line, int character, CancellationToken cancellationToken)
	{
		JsonElement rawResult = await SendRequestAsync<JsonElement>("textDocument/completion", new LspCompletionParams
		{
			TextDocument = new() { Uri = PathToUri(path) },
			Position = new() { Line = line, Character = character },
			Context = new() { TriggerKind = 1 }
		}, cancellationToken);

		return rawResult.Deserialize(LspJsonContext.Default.LspCompletionItemArray);
	}

	public async Task<LspHover?> RequestHoverAsync(string path, int line, int character, CancellationToken cancellationToken)
	{
		JsonElement rawResult = await SendRequestAsync<JsonElement>("textDocument/hover", new LspHoverParams
		{
			TextDocument = new() { Uri = PathToUri(path) },
			Position = new() { Line = line, Character = character }
		}, cancellationToken);

		return rawResult.Deserialize(LspJsonContext.Default.LspHover);
	}

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
#pragma warning disable IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.
	private async Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
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

	private Task SendNotificationAsync(string method, object? parameters = null)
	{
		LspNotification notification = new() { Method = method, Params = parameters };
		return WriteMessageAsync(notification, CancellationToken.None);
	}

	private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
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

			if (root.TryGetProperty("method", out JsonElement methodProp) &&
				root.TryGetProperty("id", out JsonElement serverRequestId))
			{
				// Server is requesting
				string method = methodProp.GetString() ?? "";
				HandleServerRequest(method, serverRequestId);
				return;
			}

			// Check if this is a response
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
			else if (root.TryGetProperty("method", out JsonElement methodNoti) && methodNoti.ValueKind == JsonValueKind.String)
			{
				// This is a notification
				string method = methodNoti.GetString() ?? "";

				if (root.TryGetProperty("params", out JsonElement param))
				{
					HandleServerNotification(method, param);
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error processing LSP message: {ex.Message}");
		}
	}

	private void HandleServerNotification(string method, JsonElement param)
	{
		if (method == "textDocument/publishDiagnostics")
		{
			LspPublishDiagnosticsParams? data = JsonSerializer.Deserialize(
				param.GetRawText(),
				LspJsonContext.Default.LspPublishDiagnosticsParams
			);

			if (data != null)
			{
				// Fix : on windows
				data.Uri = data.Uri.Replace("%3A", ":");
				PublishDiagnostics?.Invoke(data);
			}
		}
	}

	private async void HandleServerRequest(string method, JsonElement id)
	{
		try
		{
			// Handle workspace/configuration request
			if (method == "workspace/configuration")
			{
				// Return empty configuration
				LspResponse response = new()
				{
					Id = id.Clone(),
					Result = new object[] { new(), new() }
				};

				await WriteMessageAsync(response, CancellationToken.None);
			}
			else
			{
				// For other, send empty result
				LspResponse response = new()
				{
					Id = id.Clone(),
					Result = new EmptyParams()
				};

				await WriteMessageAsync(response, CancellationToken.None);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error handling server request '{method}': {ex.Message}");
		}
	}

	private static string PathToUri(string path)
	{
		return new Uri(Path.GetFullPath(path)).AbsoluteUri;
	}

	public void Dispose()
	{
		_cts.Cancel();
		_readerTask?.Wait(TimeSpan.FromSeconds(1));
		_cts.Dispose();
		_writeLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
