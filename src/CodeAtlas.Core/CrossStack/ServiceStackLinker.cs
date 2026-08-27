using CodeAtlas.Core.Db;
using CodeAtlas.Models;

namespace CodeAtlas.Core.CrossStack;

public sealed record LinkResult(int Total, int Linked, int Unmatched);

/// <summary>
/// Scans indexed symbols and populates cross_stack_mappings by matching
/// C# DTO names to TypeScript interface names using naming conventions.
/// </summary>
public sealed class ServiceStackLinker(CodeAtlasDb db)
{
    private readonly object _linkLock = new();
    private bool _isLinked;

    public void EnsureLinked()
    {
        if (_isLinked) return;
        lock (_linkLock)
        {
            if (_isLinked) return;
            LinkDtos();
            _isLinked = true;
        }
    }

    public LinkResult LinkDtos()
    {
        var csSymbols = db.Symbols.FindByType(SymbolType.Class, language: "csharp")
            .Where(s => ServiceStackPatterns.IsDtoCandidate(s.Namespace ?? s.ClassName, s.FilePath))
            .ToList();

        int linked = 0;
        int unmatched = 0;

        foreach (var csDto in csSymbols)
        {
            long? tsId = null;

            foreach (var tsName in ServiceStackPatterns.GetCandidateTsNames(csDto.SymbolName))
            {
                var tsSymbol = db.Symbols.FindByName(tsName)
                    .FirstOrDefault(s => (s.Language ?? "").Equals("typescript", StringComparison.OrdinalIgnoreCase)
                        && s.SymbolType == SymbolType.Interface);

                if (tsSymbol is null) continue;

                var angular = FindAngularConsumer(tsName);
                db.CrossStack.UpsertMapping(csDto.SymbolName, csDto.Id, tsSymbol.Id, angular);
                tsId = tsSymbol.Id;
                linked++;
                break;
            }

            if (tsId is null)
            {
                db.CrossStack.UpsertMapping(csDto.SymbolName, csDto.Id, null, null);
                unmatched++;
            }
        }

        return new LinkResult(csSymbols.Count, linked, unmatched);
    }

    private string? FindAngularConsumer(string tsInterfaceName)
    {
        var candidates = db.Symbols
            .FindByName($"%{tsInterfaceName}%")
            .Where(s => (s.Language ?? "").Equals("typescript", StringComparison.OrdinalIgnoreCase)
                && s.FilePath.Contains("component", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.FilePath)
            .FirstOrDefault();

        return candidates;
    }
}
