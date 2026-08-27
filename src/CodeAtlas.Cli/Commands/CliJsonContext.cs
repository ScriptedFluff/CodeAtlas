using System.Text.Json.Serialization;

namespace CodeAtlas.Commands;

public sealed record ParserListJsonDto(
    string Id,
    string Language,
    string Status,
    string Tier,
    IReadOnlyList<string> Extensions,
    string? Version,
    string Directory,
    string AssemblyPath,
    string? Reason);

public sealed record ParserInfoJsonDto(
    string Id,
    string Language,
    string Status,
    string? StatusReason,
    string Tier,
    string? Version,
    IReadOnlyList<string> Extensions,
    string AssemblyPath,
    string DirectoryPath,
    bool IsDisabledFolder);

public sealed record IgnoreListJsonDto(
    string RepoPath,
    string SettingsPath,
    IReadOnlyList<string> IgnorePatterns);

public sealed record QuerySymbolCliDto(
    long Id,
    string Name,
    string Type,
    string? ClassName,
    string? Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? DocComment,
    bool IsLive);

public sealed record QueryFilterCliResponse(
    string RepoPath,
    string? Filter,
    bool TranspiledToSql,
    int TotalReturned,
    bool HasMore,
    long NextCursor,
    IReadOnlyList<QuerySymbolCliDto> Symbols);

[JsonSerializable(typeof(ParserListJsonDto))]
[JsonSerializable(typeof(List<ParserListJsonDto>))]
[JsonSerializable(typeof(ParserInfoJsonDto))]
[JsonSerializable(typeof(IgnoreListJsonDto))]
[JsonSerializable(typeof(QuerySymbolCliDto))]
[JsonSerializable(typeof(QueryFilterCliResponse))]
internal sealed partial class CliJsonContext : JsonSerializerContext { }


