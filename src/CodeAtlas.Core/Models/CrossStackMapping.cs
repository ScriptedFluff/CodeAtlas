namespace CodeAtlas.Models;

public sealed record CrossStackMapping(
    string DtoName,
    string? CsSymbolName,
    string? CsFilePath,
    int? CsStartLine,
    int? CsEndLine,
    string? TsSymbolName,
    string? TsFilePath,
    int? TsStartLine,
    int? TsEndLine,
    string? AngularComponentPath);
