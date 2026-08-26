using System.Text.Json.Serialization;

namespace CodeGraphMcp.Commands;

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

[JsonSerializable(typeof(ParserListJsonDto))]
[JsonSerializable(typeof(List<ParserListJsonDto>))]
[JsonSerializable(typeof(ParserInfoJsonDto))]
internal sealed partial class CliJsonContext : JsonSerializerContext { }
