using System.Text.Json.Serialization;
using CodeGraphMcp.Mcp.Tools;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Mcp;

/// <summary>
/// AOT-safe JSON serializer context for all MCP tool result types.
/// Required so the MCP SDK's schema exporter can resolve type info without reflection.
/// </summary>
[JsonSerializable(typeof(SymbolDefinitionResult))]
[JsonSerializable(typeof(FindCallersResult))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(SearchMatch))]
[JsonSerializable(typeof(List<SearchMatch>))]
[JsonSerializable(typeof(DiffImpactResult))]
[JsonSerializable(typeof(FileImpact))]
[JsonSerializable(typeof(List<FileImpact>))]
[JsonSerializable(typeof(DtoContractResult))]
[JsonSerializable(typeof(CallerEntry))]
[JsonSerializable(typeof(List<CallerEntry>))]
[JsonSerializable(typeof(SupportedLanguagesResult))]
[JsonSerializable(typeof(SupportedLanguageInfo))]
[JsonSerializable(typeof(List<SupportedLanguageInfo>))]
[JsonSerializable(typeof(SyncFileResult))]
[JsonSerializable(typeof(SyncGitDiffResult))]
// primitive / built-ins used as tool parameters and inside records
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
internal sealed partial class McpJsonContext : JsonSerializerContext { }
