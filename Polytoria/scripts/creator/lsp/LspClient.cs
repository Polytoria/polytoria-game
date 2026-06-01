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

public class LspClient(Stream input, Stream output) : LspClientBase(input, output)
{
	public readonly Dictionary<string, string> LspPathToFull = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> FullToLspPath = new(StringComparer.OrdinalIgnoreCase);
	public event Action<LspPublishDiagnosticsParams>? PublishDiagnostics;

	public async Task InitializeAsync(string workspacePath)
	{
		LspInitializeParams initParams = new()
		{
			RootUri = LspHelper.PathToUri(workspacePath),
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
						ContentFormat = ["plaintext"]
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
		string p = LspHelper.PathToUri(path);
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
			TextDocument = new() { Uri = LspHelper.PathToUri(path) }
		});
	}

	public Task DidChangeAsync(string path, string text, int version)
	{
		return SendNotificationAsync("textDocument/didChange", new LspDidChangeParams
		{
			TextDocument = new()
			{
				Uri = LspHelper.PathToUri(path),
				Version = version
			},
			ContentChanges = [new() { Text = text }]
		});
	}

	public async Task<LspCompletionItem[]?> RequestCompletionAsync(string path, int line, int character, CancellationToken cancellationToken)
	{
		JsonElement rawResult = await SendRequestAsync<JsonElement>("textDocument/completion", new LspCompletionParams
		{
			TextDocument = new() { Uri = LspHelper.PathToUri(path) },
			Position = new() { Line = line, Character = character },
			Context = new() { TriggerKind = 1 }
		}, cancellationToken);

		return rawResult.Deserialize(LspJsonContext.Default.LspCompletionItemArray);
	}

	protected override void HandleServerNotification(string method, JsonElement param)
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

	protected override async void HandleServerRequest(string method, JsonElement id)
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
}
