using System.CommandLine;
using System.Text.Json;
using CodeAtlas.Core.Config;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Filtering;
using CodeAtlas.Core.Git;
using CodeAtlas.Models;
using CodeAtlas.Parser;
using Symbol = CodeAtlas.Models.Symbol;

namespace CodeAtlas.Commands;

/// <summary>
/// Handles CLI commands for querying symbols and knowledge graph records with pattern filtering and keyset cursor pagination:
/// 'atlas query [symbol-name]', 'atlas query symbols', and 'atlas query raw'.
/// </summary>
public static class QueryCommandHandler
{
    public static Command CreateQueryCommand()
    {
        var symbolArgument   = new Argument<string?>("symbol-name", () => null, "Name or substring of symbol to look up (optional when --filter is provided)");
        var filterOption     = new Option<string?>(new[] { "--filter", "-f" }, "Pattern or regex filter for symbol names");
        var repoOption       = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to query (defaults to current working directory or git root)");
        var dbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override directory for the repository's SQLite database");
        var typeOption       = new Option<string?>(new[] { "--type", "-t" }, "Filter by symbol type (e.g. Class, Method, Interface, Property, Enum, Function)");
        var languageOption   = new Option<string?>(new[] { "--language", "--lang" }, "Filter by programming language (e.g. csharp, typescript, html, json, yaml, css, powershell)");
        var limitOption      = new Option<int>(new[] { "--limit", "-l" }, () => 500, "Maximum number of symbols to return (defaults to 500)");
        var cursorOption     = new Option<long>(new[] { "--cursor", "-c" }, () => 0L, "Keyset pagination token (scanned database ID) to resume next page");
        var skipOption       = new Option<int>(new[] { "--skip", "--offset" }, () => 0, "Number of symbols to skip for offset pagination");
        var referencedOption = new Option<bool>(new[] { "--referenced", "--with-references" }, "Display incoming references/call sites for matched symbols");
        var jsonOption       = new Option<bool>("--json", "Output results in JSON format for scripts and autonomous agents");

        var queryCommand = new Command("query", "Query the knowledge graph for symbols with pattern filtering and cursor pagination");
        queryCommand.AddArgument(symbolArgument);
        queryCommand.AddOption(filterOption);
        queryCommand.AddOption(repoOption);
        queryCommand.AddOption(dbDirOption);
        queryCommand.AddOption(typeOption);
        queryCommand.AddOption(languageOption);
        queryCommand.AddOption(limitOption);
        queryCommand.AddOption(cursorOption);
        queryCommand.AddOption(skipOption);
        queryCommand.AddOption(referencedOption);
        queryCommand.AddOption(jsonOption);

        queryCommand.SetHandler((context) =>
        {
            var symbolArg  = context.ParseResult.GetValueForArgument(symbolArgument);
            var filterOpt  = context.ParseResult.GetValueForOption(filterOption);
            var repoOpt    = context.ParseResult.GetValueForOption(repoOption);
            var dbDir      = context.ParseResult.GetValueForOption(dbDirOption);
            var typeOpt    = context.ParseResult.GetValueForOption(typeOption);
            var langOpt    = context.ParseResult.GetValueForOption(languageOption);
            var limitVal   = context.ParseResult.GetValueForOption(limitOption);
            var cursorVal  = context.ParseResult.GetValueForOption(cursorOption);
            var skipVal    = context.ParseResult.GetValueForOption(skipOption);
            var withRefs   = context.ParseResult.GetValueForOption(referencedOption);
            var json       = context.ParseResult.GetValueForOption(jsonOption);

            ExecuteQuery(
                symbolArg: symbolArg,
                filterPattern: filterOpt,
                repoOpt: repoOpt,
                dbDir: dbDir,
                typeOpt: typeOpt,
                langOpt: langOpt,
                limitVal: limitVal,
                cursorVal: cursorVal,
                skipVal: skipVal,
                withReferences: withRefs,
                isJson: json,
                isRaw: false);
        });

        // ── query symbols ────────────────────────────────────────────────────────
        var symbolsCommand = new Command("symbols", "Query symbols matching a pattern or name filter");
        symbolsCommand.AddArgument(symbolArgument);
        symbolsCommand.AddOption(filterOption);
        symbolsCommand.AddOption(repoOption);
        symbolsCommand.AddOption(dbDirOption);
        symbolsCommand.AddOption(typeOption);
        symbolsCommand.AddOption(languageOption);
        symbolsCommand.AddOption(limitOption);
        symbolsCommand.AddOption(cursorOption);
        symbolsCommand.AddOption(skipOption);
        symbolsCommand.AddOption(referencedOption);
        symbolsCommand.AddOption(jsonOption);
        symbolsCommand.SetHandler((context) =>
        {
            var symbolArg  = context.ParseResult.GetValueForArgument(symbolArgument);
            var filterOpt  = context.ParseResult.GetValueForOption(filterOption);
            var repoOpt    = context.ParseResult.GetValueForOption(repoOption);
            var dbDir      = context.ParseResult.GetValueForOption(dbDirOption);
            var typeOpt    = context.ParseResult.GetValueForOption(typeOption);
            var langOpt    = context.ParseResult.GetValueForOption(languageOption);
            var limitVal   = context.ParseResult.GetValueForOption(limitOption);
            var cursorVal  = context.ParseResult.GetValueForOption(cursorOption);
            var skipVal    = context.ParseResult.GetValueForOption(skipOption);
            var withRefs   = context.ParseResult.GetValueForOption(referencedOption);
            var json       = context.ParseResult.GetValueForOption(jsonOption);

            ExecuteQuery(
                symbolArg: symbolArg,
                filterPattern: filterOpt,
                repoOpt: repoOpt,
                dbDir: dbDir,
                typeOpt: typeOpt,
                langOpt: langOpt,
                limitVal: limitVal,
                cursorVal: cursorVal,
                skipVal: skipVal,
                withReferences: withRefs,
                isJson: json,
                isRaw: false);
        });
        queryCommand.AddCommand(symbolsCommand);

        // ── query raw ────────────────────────────────────────────────────────────
        var rawCommand = new Command("raw", "Query raw symbol records for scripting and automation");
        rawCommand.AddOption(filterOption);
        rawCommand.AddOption(repoOption);
        rawCommand.AddOption(dbDirOption);
        rawCommand.AddOption(typeOption);
        rawCommand.AddOption(languageOption);
        rawCommand.AddOption(limitOption);
        rawCommand.AddOption(cursorOption);
        rawCommand.AddOption(skipOption);
        rawCommand.AddOption(jsonOption);
        rawCommand.SetHandler((context) =>
        {
            var filterOpt  = context.ParseResult.GetValueForOption(filterOption);
            var repoOpt    = context.ParseResult.GetValueForOption(repoOption);
            var dbDir      = context.ParseResult.GetValueForOption(dbDirOption);
            var typeOpt    = context.ParseResult.GetValueForOption(typeOption);
            var langOpt    = context.ParseResult.GetValueForOption(languageOption);
            var limitVal   = context.ParseResult.GetValueForOption(limitOption);
            var cursorVal  = context.ParseResult.GetValueForOption(cursorOption);
            var skipVal    = context.ParseResult.GetValueForOption(skipOption);
            var json       = context.ParseResult.GetValueForOption(jsonOption);

            ExecuteQuery(
                symbolArg: null,
                filterPattern: filterOpt,
                repoOpt: repoOpt,
                dbDir: dbDir,
                typeOpt: typeOpt,
                langOpt: langOpt,
                limitVal: limitVal,
                cursorVal: cursorVal,
                skipVal: skipVal,
                withReferences: false,
                isJson: json,
                isRaw: true);
        });
        queryCommand.AddCommand(rawCommand);

        return queryCommand;
    }

    private static void ExecuteQuery(
        string? symbolArg,
        string? filterPattern,
        DirectoryInfo? repoOpt,
        DirectoryInfo? dbDir,
        string? typeOpt,
        string? langOpt,
        int limitVal,
        long cursorVal,
        int skipVal,
        bool withReferences,
        bool isJson,
        bool isRaw)
    {
        var startDir = repoOpt?.FullName ?? Directory.GetCurrentDirectory();
        var repoRoot = repoOpt is not null ? startDir : (FindGitRoot(startDir) ?? startDir);
        var dbPath   = StorageConfig.ResolveDbPath(dbDir?.FullName, repoRoot);

        if (!File.Exists(dbPath))
        {
            if (isJson)
            {
                var emptyRes = new QueryFilterCliResponse(
                    RepoPath: repoRoot,
                    Filter: filterPattern ?? symbolArg,
                    TranspiledToSql: false,
                    TotalReturned: 0,
                    HasMore: false,
                    NextCursor: 0,
                    Symbols: Array.Empty<QuerySymbolCliDto>());
                Console.WriteLine(JsonSerializer.Serialize(emptyRes, CliJsonContext.Default.QueryFilterCliResponse));
            }
            else
            {
                Console.WriteLine($"No index database found at '{dbPath}'. Please run 'codeatlas index <repo-path>' first.");
            }
            return;
        }

        using var db = new CodeAtlasDb(dbPath, readOnly: true);

        var normLang = LanguageNormalizer.Normalize(langOpt);
        var parsedType = Enum.TryParse<SymbolType>(typeOpt, true, out var st) ? st : (SymbolType?)null;

        // Determine effective pattern
        FilterTranspileResult transpileResult;
        try
        {
            if (!string.IsNullOrWhiteSpace(filterPattern))
            {
                transpileResult = FilterTranspiler.Transpile(filterPattern);
            }
            else if (!string.IsNullOrWhiteSpace(symbolArg))
            {
                var kind = QueryPatternClassifier.Classify(symbolArg);
                transpileResult = kind == QueryPatternKind.PureIdentifier
                    ? FilterTranspiler.Transpile(symbolArg)
                    : FilterTranspiler.TranspilePrimaryQuery(symbolArg);
            }
            else
            {
                transpileResult = FilterTranspiler.Transpile(null);
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return;
        }

        var effectiveLimit = limitVal <= 0 ? 500 : limitVal;

        // 1. Query Dirty File Buffer
        var parsers = ParserRegistry.CreateDefault().GetAllParsers();
        var buffer  = new DirtyFileBuffer(repoRoot, parsers);
        buffer.Refresh();

        var dirtyResults = buffer.FindWithFilter(transpileResult, normLang, parsedType) ?? Array.Empty<Symbol>();
        var dirtyKeys = dirtyResults
            .Select(m => (m.FilePath.Replace('\\', '/'), m.SymbolName))
            .ToHashSet();

        // 2. Query SQLite DB
        var dbResult = db.Symbols.FindWithFilter(
            filter: transpileResult,
            cursor: cursorVal,
            limit: effectiveLimit,
            offset: skipVal,
            language: normLang,
            symbolType: parsedType);

        // 3. Merge & Deduplicate
        var mergedList = new List<QuerySymbolCliDto>();

        // Include dirty items on first page (cursor == 0 && skip == 0)
        if (cursorVal == 0 && skipVal == 0)
        {
            foreach (var sym in dirtyResults)
            {
                mergedList.Add(new QuerySymbolCliDto(
                    Id: sym.Id,
                    Name: sym.SymbolName,
                    Type: sym.SymbolType.ToString(),
                    ClassName: sym.ClassName,
                    Language: sym.Language,
                    FilePath: sym.FilePath,
                    StartLine: sym.StartLine,
                    EndLine: sym.EndLine,
                    DocComment: sym.DocComment,
                    IsLive: true));
            }
        }

        foreach (var sym in dbResult.Items)
        {
            var key = (sym.FilePath.Replace('\\', '/'), sym.SymbolName);
            if (!dirtyKeys.Contains(key))
            {
                mergedList.Add(new QuerySymbolCliDto(
                    Id: sym.Id,
                    Name: sym.SymbolName,
                    Type: sym.SymbolType.ToString(),
                    ClassName: sym.ClassName,
                    Language: sym.Language,
                    FilePath: sym.FilePath,
                    StartLine: sym.StartLine,
                    EndLine: sym.EndLine,
                    DocComment: sym.DocComment,
                    IsLive: false));
            }
        }

        // Apply limit to merged list
        if (mergedList.Count > effectiveLimit)
        {
            mergedList = mergedList.Take(effectiveLimit).ToList();
        }

        // 4. Output Formatting
        if (isJson)
        {
            var response = new QueryFilterCliResponse(
                RepoPath: repoRoot,
                Filter: transpileResult.OriginalPattern,
                TranspiledToSql: transpileResult.IsTranslatable,
                TotalReturned: mergedList.Count,
                HasMore: dbResult.HasMore,
                NextCursor: dbResult.NextCursor,
                Symbols: mergedList);

            Console.WriteLine(JsonSerializer.Serialize(response, CliJsonContext.Default.QueryFilterCliResponse));
            return;
        }

        if (mergedList.Count == 0)
        {
            var desc = !string.IsNullOrWhiteSpace(transpileResult.OriginalPattern) ? $" matching '{transpileResult.OriginalPattern}'" : "";
            Console.WriteLine($"No symbols found{desc}.");
            return;
        }

        if (isRaw)
        {
            foreach (var sym in mergedList)
            {
                var liveBadge = sym.IsLive ? " [LIVE]" : "";
                Console.WriteLine($"{sym.Id}\t{sym.Type}\t{sym.Name}\t{sym.Language}\t{sym.FilePath}:{sym.StartLine}-{sym.EndLine}{liveBadge}");
            }
            return;
        }

        foreach (var sym in mergedList)
        {
            var liveBadge = sym.IsLive ? " [LIVE]" : "";
            Console.WriteLine($"[{sym.Type}] {sym.Name} ({sym.Language}) | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}{liveBadge}");
            if (withReferences)
            {
                PrintReferences(db, sym.Name);
            }
        }

        if (dbResult.HasMore)
        {
            Console.WriteLine($"\n... more results available. Use --cursor {dbResult.NextCursor} to fetch the next page.");
        }
    }

    private static string? FindGitRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void PrintReferences(CodeAtlasDb db, string symbolName)
    {
        var callers = db.References.FindCallers(symbolName);
        if (callers.Count > 0)
        {
            Console.WriteLine($"  └── Referenced in ({callers.Count} places):");
            for (int i = 0; i < callers.Count; i++)
            {
                var caller = callers[i];
                var prefix = i == callers.Count - 1 ? "      └──" : "      ├──";
                Console.WriteLine($"{prefix} [{caller.ReferenceType}] {caller.FilePath}:{caller.LineNumber} ({caller.CallerName})");
            }
        }
        else
        {
            Console.WriteLine("  └── Referenced in (0 places)");
        }
    }
}
