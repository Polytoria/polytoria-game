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

public class StyLuaClient(Stream input, Stream output) : LspClientBase(input, output)
{
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

		try
		{
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

			// TODO: Allow these format options to be changed with editor settings
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

			// remove script from StyLua memory after use
			await SendNotificationAsync("textDocument/didClose", new LspDidCloseParams
			{
				TextDocument = new LspTextDocumentIdentifier
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
		catch (Exception ex)
		{
			PT.PrintErr($"StyLua Error: {ex.Message}");
			return text;
		}

	}
}
