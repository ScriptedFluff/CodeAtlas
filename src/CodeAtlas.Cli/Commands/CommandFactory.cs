using System.CommandLine;
using CodeAtlas.Core.Config;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Models;
using CodeAtlas.Parser;

namespace CodeAtlas.Commands;

public static class CommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        var repoArgument     = new Argument<DirectoryInfo>("repo-path", "Path to the repository root to index");
        var dbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override directory for the repository's SQLite database (defaults to <repo>/.codeatlas/)");
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
            using var db = new CodeAtlasDb(dbPath);
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
        var queryCommand = QueryCommandHandler.CreateQueryCommand();


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
            var serverExeName = OperatingSystem.IsWindows() ? "CodeAtlas.Mcp.exe" : "CodeAtlas.Mcp";
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
                Console.Error.WriteLine($"CodeAtlas.Mcp binary not found at '{serverPath}'. Launch 'CodeAtlas.Mcp' executable directly for MCP stdio server mode.");
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

            using var db = new CodeAtlasDb(dbPath);
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
            using var db = new CodeAtlasDb(dbPath);
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
            var res = await CodeAtlas.Core.Db.WorktreePruner.PruneIndexedWorktreesAsync(dbDir?.FullName);
            Console.WriteLine($"Pruned {res.PrunedDatabaseCount} stale database(s), freed {res.TotalBytesFreed:N0} bytes.");
        }, dbDirOption);

        // ── sync / sync-file ────────────────────────────────────────────────────────
        var pathArgument    = new Argument<string>("path-or-glob", "File path or glob pattern to sync or purge (e.g. 'src/MyFile.cs', 'src/Services/**/*.cs')");
        var repoOption      = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path (defaults to current working directory or git root)");
        var deletePathOpt   = new Option<bool>(new[] { "--delete", "-d" }, "Purge matching files and their symbols/edges from the index");
        var contentOpt      = new Option<string?>("--content", "In-memory file content to parse instead of reading from disk (single-file mode only)");
        var syncNoIgnoreOpt = new Option<bool>("--no-ignore", "Disable .gitignore rule filtering during sync");
        var syncVerboseOpt  = new Option<bool>(new[] { "--verbose", "-v" }, "Enable verbose progress and detailed console output");


        var syncCommand = new Command("sync", "Surgically index or purge files matching a path or glob expression in the knowledge graph");
        syncCommand.AddAlias("sync-file");
        syncCommand.AddArgument(pathArgument);
        syncCommand.AddOption(repoOption);
        syncCommand.AddOption(dbDirOption);
        syncCommand.AddOption(deletePathOpt);
        syncCommand.AddOption(contentOpt);
        syncCommand.AddOption(syncNoIgnoreOpt);
        syncCommand.AddOption(syncVerboseOpt);

        syncCommand.SetHandler(async (pathOrGlob, repoOpt, dbDir, isDelete, content, noIgnore, verbose) =>
        {
            var startDir = repoOpt?.FullName ?? Directory.GetCurrentDirectory();
            var repoRoot = repoOpt is not null ? startDir : (FindGitRoot(startDir) ?? startDir);
            var dbPath   = StorageConfig.ResolveDbPath(dbDir?.FullName, repoRoot);

            using var db = new CodeAtlasDb(dbPath);
            db.Migrate();

            // If single file and in-memory content is provided, use SingleFileIndexer directly
            if (!GlobFileIndexer.IsGlobPattern(pathOrGlob) && content != null)
            {
                var singleFileIndexer = new SingleFileIndexer();
                if (isDelete)
                {
                    var result = await singleFileIndexer.DeleteFileAsync(repoRoot, db, pathOrGlob);
                    Console.WriteLine($"Deleted {result.FilePath}: {result.SymbolsDeleted} symbols, {result.ReferencesDeleted} references ({result.ElapsedMs}ms)");
                }
                else
                {
                    var result = await singleFileIndexer.UpsertFileAsync(repoRoot, db, pathOrGlob, content);
                    if (result.Message != null && result.SymbolsUpdated == 0 && result.SymbolsAdded == 0 && result.SymbolsDeleted == 0)
                    {
                        Console.WriteLine($"Sync {result.FilePath}: {result.Message} ({result.ElapsedMs}ms)");
                    }
                    else
                    {
                        Console.WriteLine($"Indexed {result.FilePath}: {result.SymbolsUpdated} updated, {result.SymbolsAdded} added, {result.SymbolsDeleted} deleted, {result.ReferencesUpdated} refs ({result.ElapsedMs}ms)");
                    }
                }
                return;
            }

            var globIndexer = new GlobFileIndexer();
            var res = await globIndexer.SyncGlobAsync(repoRoot, db, pathOrGlob, isDelete, noIgnore, verbose);
            if (isDelete)
            {
                Console.WriteLine($"Purged {res.FilesDeleted} files matching '{res.Pattern}': {res.SymbolsDeleted} symbols, {res.ReferencesDeleted} references ({res.ElapsedMs}ms)");
            }
            else
            {
                if (res.FilesProcessed == 1 && res.FilesDeleted == 0)
                {
                    Console.WriteLine($"Indexed {res.Pattern}: {res.SymbolsUpdated} updated, {res.SymbolsAdded} added, {res.SymbolsDeleted} deleted, {res.ReferencesUpdated} refs ({res.ElapsedMs}ms)");
                }
                else
                {
                    Console.WriteLine($"Synced {res.FilesProcessed} files, {res.FilesDeleted} purged matching '{res.Pattern}': {res.SymbolsAdded} added, {res.SymbolsUpdated} updated, {res.SymbolsDeleted} deleted, {res.ReferencesUpdated} refs ({res.ElapsedMs}ms)");
                }
            }
        }, pathArgument, repoOption, dbDirOption, deletePathOpt, contentOpt, syncNoIgnoreOpt, syncVerboseOpt);

        // ── root ───────────────────────────────────────────────────────────────────
        var rootCommand = new RootCommand("CodeAtlas — local code knowledge graph engine");
        rootCommand.AddCommand(indexCommand);
        rootCommand.AddCommand(queryCommand);
        rootCommand.AddCommand(serveCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(cleanCommand);
        rootCommand.AddCommand(watchCommand);
        rootCommand.AddCommand(pruneCommand);
        rootCommand.AddCommand(syncCommand);
        rootCommand.AddCommand(ParsersCommandHandler.CreateParsersCommand());
        rootCommand.AddCommand(IgnoreCommandHandler.CreateIgnoreCommand());

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
