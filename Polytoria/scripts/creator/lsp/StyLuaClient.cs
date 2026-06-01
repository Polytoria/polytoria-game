// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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

public class StyLuaClient : IDisposable
{
	private readonly Stream _input;
	private readonly Stream _output;
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	private readonly Task? _readerTask;
	private readonly CancellationTokenSource _cts = new();

	private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = [];

	private int _requestId;

	public StyLuaClient(Stream input, Stream output)
	{
		_input = input;
		_output = output;
		_readerTask = Task.Run(ReadMessagesAsync);
	}

	public async Task InitializeAsync(string workspacePath)
	{
		// playing around
		LspInitializeParams initParams = new()
		{
			RootUri = LspHelper.PathToUri(workspacePath),
			ProcessId = System.Environment.ProcessId,
			Capabilities = new()
			{
				TextDocument = new()
				{
					Completion = new()
					{
						CompletionItem = new()
						{
							LabelDetailsSupport = true
						}
					}
				}
			},

		};

		await SendRequestAsync<LspInitializeResult>("initialize", initParams);
		await SendNotificationAsync("initialized", new EmptyParams());
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

	private void ProcessMessage(string json)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

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
			// no need for notification replies for now
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error processing StyLua LSP message: {ex.Message}");
		}
	}

	private static string ApplyTextEdits(string originalText, List<LspTextEdit> edits)
	{
		if (edits == null || edits.Count == 0) return originalText;

		static int GetFlatIndex(string text, int line, int character)
		{
			int currentLine = 0;
			int i = 0;
			while (i < text.Length && currentLine < line)
			{
				if (text[i] == '\r')
				{
					if (i + 1 < text.Length && text[i + 1] == '\n') i++;
					currentLine++;
				}
				else if (text[i] == '\n')
				{
					currentLine++;
				}
				i++;
			}
			return i + character;
		}

		var concreteEdits = new List<(int StartIdx, int EndIdx, string NewText)>();
		foreach (var edit in edits)
		{
			if (edit == null) continue;
			int startIdx = GetFlatIndex(originalText, edit.Range.Start.Line, edit.Range.Start.Character);
			int endIdx = GetFlatIndex(originalText, edit.Range.End.Line, edit.Range.End.Character);
			concreteEdits.Add((startIdx, endIdx, edit.NewText!));
		}

		concreteEdits.Sort((a, b) => b.StartIdx.CompareTo(a.StartIdx));

		string mutatedText = originalText;
		foreach (var (StartIdx, EndIdx, NewText) in concreteEdits)
		{
			int lengthToRemove = EndIdx - StartIdx;
			mutatedText = mutatedText.Remove(StartIdx, lengthToRemove);
			mutatedText = mutatedText.Insert(StartIdx, NewText);
		}

		return mutatedText;
	}

	public async Task<string> FormatScript(string path, string languageId, string text)
	{
		string p = LspHelper.PathToUri(path);

		await SendNotificationAsync("textDocument/didOpen", new LspDidOpenParams
		{
			TextDocument = new LspTextDocumentItem
			{
				Uri = p,
				LanguageId = languageId,
				Version = 1,
				Text = text
			}
		});

		// TODO: no-arr: Allow these format options to be changed
		JsonElement rawResult = await SendRequestAsync<JsonElement>("textDocument/formatting", new LspDocumentFormattingParams()
		{
			TextDocument = new()
			{
				Uri = p,
			},

			Options = new()
			{
				TabSize = 4,
				InsertSpaces = false
			}
		});

		// remove script from StyLua memory
		await SendNotificationAsync("textDocument/didClose", new LspDidOpenParams
		{
			TextDocument = new LspTextDocumentItem
			{
				Uri = p,
			}
		});

		List<LspTextEdit>? result = rawResult.Deserialize(LspJsonContext.Default.ListLspTextEdit);

		if (result != null)
		{
			return ApplyTextEdits(text, result);
		}

		return text;
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
