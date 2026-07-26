// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Creator.LSP.Schemas;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Creator.LSP;

public class LspClient(Stream input, Stream output) : LspClientBase(input, output)
{
	private static readonly string[] PluginPaths =
	[
		"./.poly/luau/polytoria-require.luau",
		"./.poly/luau/polytoria-module-types.luau"
	];

	private string _definitionFilePath = "";

	public readonly Dictionary<string, string> LspPathToFull = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> FullToLspPath = new(StringComparer.OrdinalIgnoreCase);
	public event Action<LspPublishDiagnosticsParams>? PublishDiagnostics;

	public async Task InitializeAsync(string workspacePath)
	{
		_definitionFilePath = Path.GetFullPath(Path.Join(workspacePath, ".poly", "luau", "def.d.luau"));

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
		string uri = LspHelper.PathToUri(path);
		LspPathToFull[uri] = path;
		FullToLspPath[path] = uri;

		return SendNotificationAsync("textDocument/didOpen", new LspDidOpenParams
		{
			TextDocument = new LspTextDocumentItem
			{
				Uri = uri,
				LanguageId = languageId,
				Version = 1,
				Text = text
			}
		});
	}

	public Task DidCloseAsync(string path)
	{
		if (FullToLspPath.Remove(path, out string? uri))
		{
			LspPathToFull.Remove(uri);
		}

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

		if (rawResult.ValueKind == JsonValueKind.Array)
		{
			return rawResult.Deserialize(LspJsonContext.Default.LspCompletionItemArray);
		}

		if (rawResult.ValueKind == JsonValueKind.Object &&
			rawResult.TryGetProperty("items", out JsonElement items) &&
			items.ValueKind == JsonValueKind.Array)
		{
			return items.Deserialize(LspJsonContext.Default.LspCompletionItemArray);
		}

		return null;
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
				data.Uri = data.Uri.Replace("%3A", ":");
				PublishDiagnostics?.Invoke(data);
			}
		}
		else if ((method == "window/logMessage" || method == "window/showMessage") &&
			param.ValueKind == JsonValueKind.Object &&
			param.TryGetProperty("message", out JsonElement message))
		{
			PT.Print("Luau LSP: ", message.GetString() ?? "");
		}
	}

	private Dictionary<string, object> CreateLuauConfiguration()
	{
		return new()
		{
			["platform"] = new Dictionary<string, object>
			{
				["type"] = "standard"
			},
			["types"] = new Dictionary<string, object>
			{
				// workspace/configuration may be requested for both the global scope and
				// the workspace folder. Use an absolute path so either configuration loads
				// the generated Polytoria API definitions from the correct project.
				["definitionFiles"] = new Dictionary<string, object>
				{
					["@poly"] = _definitionFilePath
				}
			},
			["completion"] = new Dictionary<string, object>
			{
				// Fragment autocomplete reuses the previous dependency graph. A source
				// transform can change the require target after an Instance move, so the
				// full completion typecheck is required to rebuild that graph correctly.
				["enableFragmentAutocomplete"] = false
			},
			["plugins"] = new Dictionary<string, object>
			{
				["enabled"] = true,
				["paths"] = PluginPaths,
				["fileSystem"] = new Dictionary<string, object>
				{
					["enabled"] = true
				}
			}
		};
	}

	protected override async void HandleServerRequest(string method, JsonElement id, JsonElement? param)
	{
		try
		{
			if (method == "workspace/configuration")
			{
				int configurationCount = 1;
				if (param.HasValue &&
					param.Value.ValueKind == JsonValueKind.Object &&
					param.Value.TryGetProperty("items", out JsonElement items) &&
					items.ValueKind == JsonValueKind.Array)
				{
					configurationCount = items.GetArrayLength();
				}

				object[] configurations = new object[configurationCount];
				for (int index = 0; index < configurations.Length; index++)
				{
					configurations[index] = CreateLuauConfiguration();
				}

				PT.Print("Luau LSP requested ", configurationCount, " configuration entries; enabling ", PluginPaths.Length, " Polytoria plugins and definitions");

				await WriteMessageAsync(new LspResponse
				{
					Id = id.Clone(),
					Result = configurations
				}, CancellationToken.None);
			}
			else
			{
				await WriteMessageAsync(new LspResponse
				{
					Id = id.Clone(),
					Result = new EmptyParams()
				}, CancellationToken.None);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error handling server request '{method}': {ex.Message}");
		}
	}
}
