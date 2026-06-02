// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Creator.LSP.Schemas;

public class LspRequest
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";
	[JsonPropertyName("id")]
	public int Id { get; set; }
	[JsonPropertyName("method")]
	public string Method { get; set; } = "";
	[JsonPropertyName("params")]
	public object? Params { get; set; }
}

public class LspNotification
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";
	[JsonPropertyName("method")]
	public string Method { get; set; } = "";
	[JsonPropertyName("params")]
	public object? Params { get; set; }
}

public class LspInitializeParams
{
	[JsonPropertyName("rootUri")]
	public string? RootUri { get; set; }

	[JsonPropertyName("capabilities")]
	public LspClientCapabilities? Capabilities { get; set; }

	[JsonPropertyName("initializationOptions")]
	public LspInitializationOptions? InitializationOptions { get; set; }
}

public class LspInitializationOptions
{
	[JsonPropertyName("respect_editor_formatting_options")]
	public bool? RespectEditorFormattingOptions { get; set; } // Used by StyLua
}

public class LspClientCapabilities
{
	[JsonPropertyName("textDocument")]
	public LspTextDocumentCapabilities? TextDocument { get; set; }
	[JsonPropertyName("workspace")]
	public LspWorkspaceCapabilities? Workspace { get; set; }
	[JsonPropertyName("general")]
	public LspGeneralCapabilities? General { get; set; }
}

public class LspTextDocumentCapabilities
{
	[JsonPropertyName("completion")]
	public LspCompletionCapability? Completion { get; set; }
	[JsonPropertyName("hover")]
	public LspHoverCapability? Hover { get; set; }
	[JsonPropertyName("synchronization")]
	public LspSynchronizationCapability? Synchronization { get; set; }
}

public class LspCompletionCapability
{
	[JsonPropertyName("completionItem")]
	public LspCompletionItemCapability? CompletionItem { get; set; }
}

public class LspCompletionItemCapability
{
	[JsonPropertyName("snippetSupport")]
	public bool SnippetSupport { get; set; }

	[JsonPropertyName("labelDetailsSupport")]
	public bool? LabelDetailsSupport { get; set; }
}

public class LspHoverCapability
{
	[JsonPropertyName("contentFormat")]
	public string[]? ContentFormat { get; set; }
}

public class LspSynchronizationCapability
{
	[JsonPropertyName("dynamicRegistration")]
	public bool? DynamicRegistration { get; set; }
	[JsonPropertyName("didSave")]
	public bool DidSave { get; set; }
	[JsonPropertyName("willSave")]
	public bool WillSave { get; set; }
	[JsonPropertyName("willSaveWaitUntil")]
	public bool WillSaveWaitUntil { get; set; }
}

public class LspWorkspaceCapabilities
{
	[JsonPropertyName("applyEdit")]
	public bool ApplyEdit { get; set; }
	[JsonPropertyName("workspaceEdit")]
	public LspWorkspaceEditCapability? WorkspaceEdit { get; set; }
	[JsonPropertyName("configuration")]
	public bool Configuration { get; set; }
	[JsonPropertyName("didChangeWatchedFiles")]
	public LspDidChangeWatchedFilesCapabilities? DidChangeWatchedFiles { get; set; }
}

public class LspDidChangeWatchedFilesCapabilities
{
	[JsonPropertyName("dynamicRegistration")]
	public bool? DynamicRegistration { get; set; }
	[JsonPropertyName("relativePatternSupport")]
	public bool? RelativePatternSupport { get; set; }
}

public class LspWorkspaceEditCapability
{
	[JsonPropertyName("documentChanges")]
	public bool DocumentChanges { get; set; }
}

public class LspGeneralCapabilities
{
	[JsonPropertyName("positionEncodings")]
	public string[]? PositionEncodings { get; set; }
}

public class LspInitializeResult
{
	[JsonPropertyName("capabilities")]
	public object? Capabilities { get; set; }
}

public class LspTextDocumentItem
{
	[JsonPropertyName("uri")]
	public string Uri { get; set; } = "";
	[JsonPropertyName("languageId")]
	public string LanguageId { get; set; } = "";
	[JsonPropertyName("version")]
	public int Version { get; set; }
	[JsonPropertyName("text")]
	public string Text { get; set; } = "";
}

public class LspTextDocumentIdentifier
{
	[JsonPropertyName("uri")]
	public string Uri { get; set; } = "";
}

public class LspVersionedTextDocumentIdentifier : LspTextDocumentIdentifier
{
	[JsonPropertyName("version")]
	public int Version { get; set; }
}

public class LspDidOpenParams
{
	[JsonPropertyName("textDocument")]
	public LspTextDocumentItem? TextDocument { get; set; }
}

public class LspDidCloseParams
{
	[JsonPropertyName("textDocument")]
	public LspTextDocumentIdentifier? TextDocument { get; set; }
}

public class LspDidChangeParams
{
	[JsonPropertyName("textDocument")]
	public LspVersionedTextDocumentIdentifier? TextDocument { get; set; }
	[JsonPropertyName("contentChanges")]
	public LspTextDocumentContentChangeEvent[]? ContentChanges { get; set; }
}

public class LspTextDocumentContentChangeEvent
{
	[JsonPropertyName("text")]
	public string Text { get; set; } = "";
}

public class LspCompletionParams
{
	[JsonPropertyName("textDocument")]
	public LspTextDocumentIdentifier? TextDocument { get; set; }
	[JsonPropertyName("position")]
	public LspPosition? Position { get; set; }
	[JsonPropertyName("context")]
	public LspCompletionContext? Context { get; set; }
}

public class LspPosition
{
	[JsonPropertyName("line")]
	public int Line { get; set; }
	[JsonPropertyName("character")]
	public int Character { get; set; }
}

public class LspCompletionContext
{
	[JsonPropertyName("triggerKind")]
	public int TriggerKind { get; set; }
}

public class LspCompletionItem
{
	[JsonPropertyName("label")]
	public string Label { get; set; } = "";

	[JsonPropertyName("labelDetails")]
	public LspCompletionItemLabelDetails? LabelDetails { get; set; }

	[JsonPropertyName("kind")]
	public int Kind { get; set; }

	[JsonPropertyName("detail")]
	public string? Detail { get; set; }

	[JsonPropertyName("documentation")]
	public LspMarkupContent? Documentation { get; set; }

	[JsonPropertyName("preselect")]
	public bool Preselect { get; set; }

	[JsonPropertyName("sortText")]
	public string? SortText { get; set; }

	[JsonPropertyName("insertText")]
	public string? InsertText { get; set; }

	[JsonPropertyName("insertTextFormat")]
	public int InsertTextFormat { get; set; }

	[JsonPropertyName("command")]
	public LspCommand? Command { get; set; }

	[JsonPropertyName("deprecated")]
	public bool Deprecated { get; set; }

	[JsonPropertyName("additionalTextEdits")]
	public List<object>? AdditionalTextEdits { get; set; }
}

public class LspCompletionItemLabelDetails
{
	[JsonPropertyName("detail")]
	public string? Detail { get; set; }

	[JsonPropertyName("description")]
	public string? Description { get; set; }
}

public class LspDocumentFormattingParams
{
	[JsonPropertyName("textDocument")]
	public LspTextDocumentIdentifier? TextDocument { get; set; }

	[JsonPropertyName("options")]
	public LspFormattingOptions? Options { get; set; }
}

public class LspFormattingOptions
{
	[JsonPropertyName("tabSize")]
	public int TabSize { get; set; }

	[JsonPropertyName("insertSpaces")]
	public bool InsertSpaces { get; set; }

	// Optionals:

	[JsonPropertyName("trimTrailingWhitespace")]
	public bool? TrimTrailingWhitespace { get; set; }

	[JsonPropertyName("insertFinalNewline")]
	public bool? InsertFinalNewline { get; set; }

	[JsonPropertyName("trimFinalNewlines")]
	public bool? TrimFinalNewlines { get; set; }
}

public class LspTextEdit
{
	[JsonPropertyName("range")]
	public LspRange Range { get; set; } = new();

	[JsonPropertyName("newText")]
	public string? NewText { get; set; }
}

public class LspMarkupContent
{
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "markdown"; // or "plaintext"

	[JsonPropertyName("value")]
	public string Value { get; set; } = "";
}

public class LspCommand
{
	[JsonPropertyName("title")]
	public string Title { get; set; } = "";

	[JsonPropertyName("command")]
	public string Command { get; set; } = "";

	[JsonPropertyName("arguments")]
	public List<object>? Arguments { get; set; }
}

public class LspResponse
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";

	[JsonPropertyName("id")]
	public JsonElement Id { get; set; }

	[JsonPropertyName("result")]
	public object? Result { get; set; }

	[JsonPropertyName("error")]
	public object? Error { get; set; }
}

public class LspPublishDiagnosticsParams
{
	[JsonPropertyName("uri")]
	public string Uri { get; set; } = "";

	[JsonPropertyName("version")]
	public int? Version { get; set; }

	[JsonPropertyName("diagnostics")]
	public List<LspDiagnostic> Diagnostics { get; set; } = [];
}

public class LspDiagnostic
{
	[JsonPropertyName("range")]
	public LspRange Range { get; set; } = new();

	/// <summary>
	/// 1: Error, 2: Warning, 3: Information, 4: Hint
	/// </summary>
	[JsonPropertyName("severity")]
	public int? Severity { get; set; }

	[JsonPropertyName("code")]
	public object? Code { get; set; } // Can be string or int

	[JsonPropertyName("codeDescription")]
	public LspCodeDescription? CodeDescription { get; set; }

	[JsonPropertyName("source")]
	public string? Source { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; } = "";

	/// <summary>
	/// 1: Unnecessary (Unused), 2: Deprecated
	/// </summary>
	[JsonPropertyName("tags")]
	public int[]? Tags { get; set; }

	[JsonPropertyName("relatedInformation")]
	public List<LspDiagnosticRelatedInformation>? RelatedInformation { get; set; }
}

public class LspCodeDescription
{
	[JsonPropertyName("href")]
	public string Href { get; set; } = "";
}

public class LspRange
{
	[JsonPropertyName("start")]
	public LspPosition Start { get; set; } = new();

	[JsonPropertyName("end")]
	public LspPosition End { get; set; } = new();
}

public class LspDiagnosticRelatedInformation
{
	[JsonPropertyName("location")]
	public LspLocation Location { get; set; } = new();

	[JsonPropertyName("message")]
	public string Message { get; set; } = "";
}

public class LspLocation
{
	[JsonPropertyName("uri")]
	public string Uri { get; set; } = "";

	[JsonPropertyName("range")]
	public LspRange Range { get; set; } = new();
}

public sealed class EmptyParams
{
}

[JsonSourceGenerationOptions(
	WriteIndented = false,
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LspRequest))]
[JsonSerializable(typeof(LspNotification))]
[JsonSerializable(typeof(LspResponse))]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(LspInitializeParams))]
[JsonSerializable(typeof(LspInitializeResult))]
[JsonSerializable(typeof(LspInitializationOptions))]
[JsonSerializable(typeof(LspDidOpenParams))]
[JsonSerializable(typeof(LspDidChangeParams))]
[JsonSerializable(typeof(LspDidCloseParams))]
[JsonSerializable(typeof(LspTextDocumentContentChangeEvent[]))]
[JsonSerializable(typeof(LspCompletionParams))]
[JsonSerializable(typeof(List<LspCompletionItem>))]
[JsonSerializable(typeof(LspCompletionItem))]
[JsonSerializable(typeof(LspCompletionItemLabelDetails))]
[JsonSerializable(typeof(LspMarkupContent))]
[JsonSerializable(typeof(LspCommand))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(LspCompletionItem[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(LspPublishDiagnosticsParams))]
[JsonSerializable(typeof(LspDiagnostic))]
[JsonSerializable(typeof(List<LspDiagnostic>))]
[JsonSerializable(typeof(LspRange))]
[JsonSerializable(typeof(LspLocation))]
[JsonSerializable(typeof(LspCodeDescription))]
[JsonSerializable(typeof(LspDocumentFormattingParams))]
[JsonSerializable(typeof(LspFormattingOptions))]
[JsonSerializable(typeof(LspTextEdit))]
[JsonSerializable(typeof(List<LspTextEdit>))]
internal partial class LspJsonContext : JsonSerializerContext { }
