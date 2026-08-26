using System.CommandLine;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Commands;

public static class CommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        var repoArgument     = new Argument<DirectoryInfo>("repo-path", "Path to the repository root to index");
        var dbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override directory for the repository's SQLite database (defaults to <repo>/.codegraph/)");
        var forceFullOption  = new Option<bool>("--force-full", "Skip delta detection and re-index everything");
        var noIgnoreOption   = new Option<bool>("--no-ignore", "Disable .gitignore rule filtering during indexing");
        var verboseOption    = new Option<bool>(new[] { "--verbose", "-v" }, "Enable verbose progress and detailed console output");

        // ── index ──────────────────────────────────────────────────────────────────
        var indexCommand = new Command("index", "Parse and index a repository into the knowledge graph");
        indexCommand.AddArgument(repoArgument);
        indexCommand.AddOption(dbDirOption);
        indexCommand.AddOption(forceFullOption);
        indexCommand.AddOption(noIgnoreOption);
        indexCommand.AddOption(verboseOption);
        indexCommand.SetHandler(async (repo, dbDir, force, noIgnore, verbose) =>
        {
            var normRepo = WorkspacePathResolver.NormalizePath(repo.FullName);
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, normRepo);
            using var db = new CodeGraphDb(dbPath);
            db.Migrate();

            Console.WriteLine($"Database:         {dbPath}");

            var registry = ParserRegistry.CreateDefault();

            if (force || !GitHelpers.IsGitRepo(normRepo))
            {
                var orchestrator = new IndexOrchestrator(db, registry);
                var result = await orchestrator.IndexAsync(normRepo, noIgnore, verbose);
                var headHash = GitHelpers.GetHeadCommitHash(normRepo) ?? "unknown";
                var branch = GitHelpers.GetCurrentBranch(normRepo) ?? "unknown";
                db.IndexState.SetIndexState(branch, headHash, 0);

                Console.WriteLine($"Indexed {result.FilesProcessed} files, {result.SymbolsWritten} symbols ({result.ElapsedMs}ms) [full]");
            }
            else
            {
                var delta   = new DeltaEngine(normRepo, db, registry);
                var result  = await delta.RunAsync(noIgnore, verbose);

                var seedCommitInfo = result.SeedCommitHash is { Length: >= 7 } h ? $" (commit {h[..7]})" : "";
                var seedInfo = result.SeedSourcePath is not null ? $"Seeded index from '{result.SeedSourcePath}'{seedCommitInfo}. " : "";

                Console.WriteLine(result.Mode switch
                {
                    "noop"         => $"Nothing to index — HEAD unchanged ({result.ElapsedMs}ms) [noop]",
                    "full"         => $"Indexed {result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms) [full]",
                    "delta"        => $"{result.FilesChanged} changed, {result.FilesReindexed} re-indexed, {result.SymbolsAdded} symbols added ({result.ElapsedMs}ms) [delta]",
                    "seeded-delta" => $"{seedInfo}{result.FilesChanged} changed, {result.FilesReindexed} re-indexed, {result.SymbolsAdded} symbols added ({result.ElapsedMs}ms) [seeded-delta]",
                    _              => $"{seedInfo}Done ({result.ElapsedMs}ms)"
                });
            }
        }, repoArgument, dbDirOption, forceFullOption, noIgnoreOption, verboseOption);


        // ── query ──────────────────────────────────────────────────────────────────
        var symbolArgument   = new Argument<string>("symbol-name", "Name of the symbol to look up");
        var repoOption       = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to query (defaults to current working directory)");
        var referencedOption = new Option<bool>(new[] { "--referenced", "--with-references" }, "Display incoming references/call sites for matched symbols");
        var typeOption       = new Option<string?>(new[] { "--type", "-t" }, "Filter by symbol type (e.g. Class, Method, Interface, Property, Enum, Function)");
        var languageOption   = new Option<string?>(new[] { "--language", "--lang" }, "Filter by programming language (e.g. csharp, typescript, html, json, yaml, css, powershell)");
        var limitOption      = new Option<int>(new[] { "--limit" }, () => 0, "Maximum number of symbols to return (0 or negative for all matches)");
        var skipOption       = new Option<int>(new[] { "--skip", "--offset" }, () => 0, "Number of symbols to skip for pagination");
        var queryCommand     = new Command("query", "Query the knowledge graph for a symbol");
        queryCommand.AddArgument(symbolArgument);
        queryCommand.AddOption(repoOption);
        queryCommand.AddOption(typeOption);
        queryCommand.AddOption(languageOption);
        queryCommand.AddOption(limitOption);
        queryCommand.AddOption(skipOption);
        queryCommand.AddOption(dbDirOption);
        queryCommand.AddOption(referencedOption);
        queryCommand.SetHandler((context) =>
        {
            var symbol         = context.ParseResult.GetValueForArgument(symbolArgument);
            var repoOpt        = context.ParseResult.GetValueForOption(repoOption);
            var typeOpt        = context.ParseResult.GetValueForOption(typeOption);
            var langOpt        = context.ParseResult.GetValueForOption(languageOption);
            var limitVal       = context.ParseResult.GetValueForOption(limitOption);
            var skipVal        = context.ParseResult.GetValueForOption(skipOption);
            var dbDir          = context.ParseResult.GetValueForOption(dbDirOption);
            var withReferences = context.ParseResult.GetValueForOption(referencedOption);

            var startDir = repoOpt?.FullName ?? Directory.GetCurrentDirectory();
            var repoRoot = repoOpt is not null ? startDir : (FindGitRoot(startDir) ?? startDir);
            var dbPath   = StorageConfig.ResolveDbPath(dbDir?.FullName, repoRoot);

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"No index database found at '{dbPath}'. Please run 'index <repo-path>' first.");
                return;
            }

            using var db = new CodeGraphDb(dbPath, readOnly: true);

            var normLang = LanguageNormalizer.Normalize(langOpt);
            var parsedType = Enum.TryParse<SymbolType>(typeOpt, true, out var st) ? st : (SymbolType?)null;

            var parsers = GetParsers();
            var buffer  = new DirtyFileBuffer(repoRoot, parsers);
            buffer.Refresh();

            var dirtyResults = buffer.FindByName(symbol, normLang);
            if (dirtyResults is not null && dirtyResults.Count > 0)
            {
                var filteredDirty = dirtyResults
                    .Where(s => parsedType is null || s.SymbolType == parsedType.Value)
                    .Where(s => normLang is null || (s.Language ?? "").Equals(normLang, StringComparison.OrdinalIgnoreCase))
                    .Skip(Math.Max(0, skipVal));

                if (limitVal > 0)
                {
                    filteredDirty = filteredDirty.Take(limitVal);
                }

                var dirtyList = filteredDirty.ToList();
                if (dirtyList.Count > 0)
                {
                    Console.WriteLine($"[LIVE - uncommitted]");
                    foreach (var sym in dirtyList)
                    {
                        Console.WriteLine($"  [{sym.SymbolType}] {sym.SymbolName} ({sym.Language}) | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
                        if (withReferences)
                        {
                            PrintReferences(db, sym.SymbolName);
                        }
                    }
                    return;
                }
            }

            var results = db.Symbols.FindByName(
                symbol,
                limit: limitVal,
                offset: skipVal,
                language: normLang,
                symbolType: parsedType);

            if (results.Count == 0)
            {
                Console.WriteLine($"No symbols found matching '{symbol}'.");
                return;
            }
            foreach (var sym in results)
            {
                Console.WriteLine($"[{sym.SymbolType}] {sym.SymbolName} ({sym.Language}) | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
                if (withReferences)
                {
                    PrintReferences(db, sym.SymbolName);
                }
            }
        });

        // ── serve ──────────────────────────────────────────────────────────────────
        var serveRepoArg     = new Argument<DirectoryInfo?>("repo-path", () => null, "Repository path to serve (optional)");
        var serveRepoOption  = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to serve (optional, defaults to unbound/dynamic target directories)");
        var serveDbDirOption = new Option<DirectoryInfo?>("--db-dir", "Override SQLite database directory");

        var serveCommand = new Command("serve", "Start the MCP stdio server");
        serveCommand.AddArgument(serveRepoArg);
        serveCommand.AddOption(serveRepoOption);
        serveCommand.AddOption(serveDbDirOption);

        serveCommand.SetHandler(async (context) =>
        {
            var repoArg = context.ParseResult.GetValueForArgument(serveRepoArg);
            var repoOpt = context.ParseResult.GetValueForOption(serveRepoOption);
            var dbDir   = context.ParseResult.GetValueForOption(serveDbDirOption);

            var repoPath = repoArg?.FullName ?? repoOpt?.FullName;

            var baseDir = AppContext.BaseDirectory;
            var serverExeName = OperatingSystem.IsWindows() ? "CodeGraphMcp.Server.exe" : "CodeGraphMcp.Server";
            var serverPath = Path.Combine(baseDir, serverExeName);

            var argsList = new List<string>();
            if (!string.IsNullOrWhiteSpace(repoPath))
            {
                argsList.Add("--repo");
                argsList.Add($"\"{repoPath}\"");
            }
            if (dbDir != null)
            {
                argsList.Add("--db-dir");
                argsList.Add($"\"{dbDir.FullName}\"");
            }

            if (File.Exists(serverPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(serverPath, string.Join(" ", argsList))
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(context.GetCancellationToken());
                }
            }
            else
            {
                Console.Error.WriteLine($"CodeGraphMcp.Server binary not found at '{serverPath}'. Launch 'CodeGraphMcp.Server' executable directly for MCP stdio server mode.");
            }
        });

        // ── status ─────────────────────────────────────────────────────────────────
        var statusCommand = new Command("status", "Show the current index state for a repository");
        statusCommand.AddArgument(repoArgument);
        statusCommand.AddOption(dbDirOption);
        statusCommand.SetHandler((repo, dbDir) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, repo.FullName);
            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"No database found at '{dbPath}'.");
                return;
            }

            using var db = new CodeGraphDb(dbPath);
            db.Migrate();

            var lastHash = db.IndexState.GetLastCommitHash();
            var headHash = GitHelpers.GetHeadCommitHash(repo.FullName) ?? "unknown";
            var branch   = GitHelpers.GetCurrentBranch(repo.FullName) ?? "unknown";
            var counts   = db.Symbols.CountPerFile();
            var fileCount   = counts.Count;
            var symbolCount = counts.Values.Sum();
            var refCount    = db.References.Count();

            var activeParsers = ParserRegistry.CreateDefault().GetAllParsers();
            var indexedParsers = db.IndexedParsers.GetIndexedParsers();
            var indexedMap = indexedParsers.ToDictionary(p => p.ParserId, StringComparer.OrdinalIgnoreCase);

            var unindexed = activeParsers.Where(p => !indexedMap.ContainsKey(p.Metadata.Id)).Select(p => p.Metadata.Id).ToList();
            var needsReindex = lastHash is null ||
                               (headHash != "unknown" && !string.Equals(headHash, lastHash, StringComparison.OrdinalIgnoreCase)) ||
                               unindexed.Count > 0;

            var indexedSummary = indexedParsers.Count > 0 
                ? string.Join(", ", indexedParsers.Select(p => p.ParserId)) 
                : (fileCount > 0 ? "legacy (upgrade on next index)" : "none");

            Console.WriteLine($"Repository:       {repo.FullName}");
            Console.WriteLine($"Database:         {dbPath}");
            Console.WriteLine($"Branch:           {branch}");
            Console.WriteLine($"HEAD:             {headHash}");
            Console.WriteLine($"Last Indexed:     {lastHash ?? "never"}");
            Console.WriteLine($"Needs Re-index:   {(needsReindex ? (unindexed.Count > 0 ? $"yes (new unindexed parsers: {string.Join(", ", unindexed)})" : "yes") : "no")}");
            Console.WriteLine($"Indexed Parsers:  {indexedSummary}");
            Console.WriteLine($"Files Indexed:    {fileCount}");
            Console.WriteLine($"Symbols Indexed:  {symbolCount}");
            Console.WriteLine($"References:       {refCount}");
        }, repoArgument, dbDirOption);

        // ── clean ──────────────────────────────────────────────────────────────────
        var forceOption = new Option<bool>(new[] { "--force", "-f" }, "Skip confirmation prompt");
        var yesOption   = new Option<bool>(new[] { "--yes", "-y" }, "Skip confirmation prompt");

        var cleanCommand = new Command("clean", "Delete the index database for a repository");
        cleanCommand.AddArgument(repoArgument);
        cleanCommand.AddOption(dbDirOption);
        cleanCommand.AddOption(forceOption);
        cleanCommand.AddOption(yesOption);
        cleanCommand.SetHandler((repo, dbDir, force, yes) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, repo.FullName);
            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"No database found at '{dbPath}'.");
                return;
            }

            if (!force && !yes)
            {
                Console.Write($"Delete '{dbPath}'? [y/N] ");
                var response = Console.ReadLine();
                if (!string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Cancelled.");
                    return;
                }
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            Console.WriteLine($"Deleted '{dbPath}'.");
        }, repoArgument, dbDirOption, forceOption, yesOption);

        // ── watch ──────────────────────────────────────────────────────────────────
        var intervalOption = new Option<int>("--interval", () => 30, "Seconds between index checks");

        var watchCommand = new Command("watch", "Continuously re-index on git changes");
        watchCommand.AddArgument(repoArgument);
        watchCommand.AddOption(dbDirOption);
        watchCommand.AddOption(intervalOption);
        watchCommand.SetHandler(async (repo, dbDir, interval) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, repo.FullName);
            using var db = new CodeGraphDb(dbPath);
            db.Migrate();

            var parsers = GetParsers();
            var delta   = new DeltaEngine(repo.FullName, db, parsers);

            Console.WriteLine($"Watching '{repo.FullName}' every {interval}s. Press Ctrl+C to stop.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await delta.RunAsync(cts.Token);
                    if (result.Mode != "noop")
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {result.Mode}: " +
                            $"{result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms)");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            Console.WriteLine("Stopped.");
        }, repoArgument, dbDirOption, intervalOption);

        // ── prune ──────────────────────────────────────────────────────────────────
        var pruneCommand = new Command("prune", "Remove stale index databases for non-existent repositories or worktrees");
        pruneCommand.AddOption(dbDirOption);
        pruneCommand.SetHandler(async (dbDir) =>
        {
            var res = await CodeGraphMcp.Db.WorktreePruner.PruneIndexedWorktreesAsync(dbDir?.FullName);
            Console.WriteLine($"Pruned {res.PrunedDatabaseCount} stale database(s), freed {res.TotalBytesFreed:N0} bytes.");
        }, dbDirOption);

        // ── root ───────────────────────────────────────────────────────────────────
        var rootCommand = new RootCommand("CodeGraphMcp — local code knowledge graph engine");
        rootCommand.AddCommand(indexCommand);
        rootCommand.AddCommand(queryCommand);
        rootCommand.AddCommand(serveCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(cleanCommand);
        rootCommand.AddCommand(watchCommand);
        rootCommand.AddCommand(pruneCommand);
        rootCommand.AddCommand(ParsersCommandHandler.CreateParsersCommand());

        return rootCommand;
    }

    private static IReadOnlyList<ILanguageParser> GetParsers() =>
        ParserRegistry.CreateDefault().GetAllParsers();

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

    private static void PrintReferences(CodeGraphDb db, string symbolName)
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
