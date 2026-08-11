using System.CommandLine;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Commands;

public static class CommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        var repoArgument     = new Argument<DirectoryInfo>("repo-path", "Path to the repository root to index");
        var dbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override directory for the SQLite database");
        var storeLocalOption = new Option<bool>(new[] { "--store-local", "-l" }, "Store the database inside the repo under .agent-index/");
        var forceFullOption  = new Option<bool>("--force-full", "Skip delta detection and re-index everything");
        var noIgnoreOption   = new Option<bool>("--no-ignore", "Disable .gitignore rule filtering during indexing");
        var verboseOption    = new Option<bool>(new[] { "--verbose", "-v" }, "Enable verbose progress and detailed console output");

        // ── index ──────────────────────────────────────────────────────────────────
        var indexCommand = new Command("index", "Parse and index a repository into the knowledge graph");
        indexCommand.AddArgument(repoArgument);
        indexCommand.AddOption(dbDirOption);
        indexCommand.AddOption(storeLocalOption);
        indexCommand.AddOption(forceFullOption);
        indexCommand.AddOption(noIgnoreOption);
        indexCommand.AddOption(verboseOption);
        indexCommand.SetHandler(async (repo, dbDir, storeLocal, force, noIgnore, verbose) =>
        {
            var normRepo = WorkspacePathResolver.NormalizePath(repo.FullName);
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, normRepo);
            using var db = new CodeGraphDb(dbPath);
            db.Migrate();

            if (force || !GitHelpers.IsGitRepo(normRepo))
            {
                var orchestrator = new IndexOrchestrator(db);
                var result = await orchestrator.IndexAsync(normRepo, noIgnore, verbose);
                var headHash = GitHelpers.GetHeadCommitHash(normRepo) ?? "unknown";
                var branch = GitHelpers.GetCurrentBranch(normRepo) ?? "unknown";
                db.IndexState.SetIndexState(branch, headHash, 0);

                Console.WriteLine($"Indexed {result.FilesProcessed} files, {result.SymbolsWritten} symbols ({result.ElapsedMs}ms) [full]");
            }
            else
            {
                var parsers = GetParsers();
                var delta   = new DeltaEngine(normRepo, db, parsers);
                var result  = await delta.RunAsync(noIgnore, verbose);

                Console.WriteLine(result.Mode switch
                {
                    "noop"  => $"Nothing to index — HEAD unchanged ({result.ElapsedMs}ms) [noop]",
                    "full"  => $"Indexed {result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms) [full]",
                    "delta" => $"{result.FilesChanged} changed, {result.FilesReindexed} re-indexed, {result.SymbolsAdded} symbols added ({result.ElapsedMs}ms) [delta]",
                    _       => $"Done ({result.ElapsedMs}ms)"
                });
            }
        }, repoArgument, dbDirOption, storeLocalOption, forceFullOption, noIgnoreOption, verboseOption);

        // ── query ──────────────────────────────────────────────────────────────────
        var symbolArgument = new Argument<string>("symbol-name", "Name of the symbol to look up");
        var repoOption     = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to query (defaults to current working directory)");
        var queryCommand   = new Command("query", "Query the knowledge graph for a symbol");
        queryCommand.AddArgument(symbolArgument);
        queryCommand.AddOption(repoOption);
        queryCommand.AddOption(dbDirOption);
        queryCommand.AddOption(storeLocalOption);
        queryCommand.SetHandler((symbol, repoOpt, dbDir, storeLocal) =>
        {
            var startDir = repoOpt?.FullName ?? Directory.GetCurrentDirectory();
            var repoRoot = repoOpt is not null ? startDir : (FindGitRoot(startDir) ?? startDir);
            var dbPath   = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repoRoot);

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"No index database found at '{dbPath}'. Please run 'index <repo-path>' first.");
                return;
            }

            using var db = new CodeGraphDb(dbPath, readOnly: true);

            var parsers = GetParsers();
            var buffer  = new DirtyFileBuffer(repoRoot, parsers);
            buffer.Refresh();

            var dirtyResults = buffer.FindByName(symbol);
            if (dirtyResults is not null && dirtyResults.Count > 0)
            {
                Console.WriteLine($"[LIVE - uncommitted]");
                foreach (var sym in dirtyResults)
                    Console.WriteLine($"  [{sym.SymbolType}] {sym.SymbolName} | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
                return;
            }

            var results = db.Symbols.FindByName(symbol);
            if (results.Count == 0)
            {
                Console.WriteLine($"No symbols found matching '{symbol}'.");
                return;
            }
            foreach (var sym in results)
            {
                Console.WriteLine($"[{sym.SymbolType}] {sym.SymbolName} | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
            }
        }, symbolArgument, repoOption, dbDirOption, storeLocalOption);

        // ── serve ──────────────────────────────────────────────────────────────────
        var serveRepoArg          = new Argument<DirectoryInfo?>("repo-path", () => null, "Repository path to serve (optional)");
        var serveRepoOption       = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to serve (optional, defaults to unbound/dynamic target directories)");
        var serveDbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override SQLite database directory");
        var serveStoreLocalOption = new Option<bool>(new[] { "--store-local", "-l" }, "Use .agent-index/ in repo");

        var serveCommand = new Command("serve", "Start the MCP stdio server");
        serveCommand.AddArgument(serveRepoArg);
        serveCommand.AddOption(serveRepoOption);
        serveCommand.AddOption(serveDbDirOption);
        serveCommand.AddOption(serveStoreLocalOption);

        serveCommand.SetHandler(async (context) =>
        {
            var repoArg    = context.ParseResult.GetValueForArgument(serveRepoArg);
            var repoOpt    = context.ParseResult.GetValueForOption(serveRepoOption);
            var dbDir      = context.ParseResult.GetValueForOption(serveDbDirOption);
            var storeLocal = context.ParseResult.GetValueForOption(serveStoreLocalOption);

            var repoPath = repoArg?.FullName ?? repoOpt?.FullName;
            if (storeLocal && repoPath is null)
            {
                Console.Error.WriteLine("Warning: --store-local (-l) requires repo-path to locate repository root. Ignoring --store-local for unbound mode.");
            }

            var dbPath = repoPath is not null ? StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repoPath) : dbDir?.FullName;
            await CodeGraphMcp.Mcp.McpServer.RunAsync(repoPath, dbPath, context.GetCancellationToken());
        });

        // ── status ─────────────────────────────────────────────────────────────────
        var statusCommand = new Command("status", "Show the current index state for a repository");
        statusCommand.AddArgument(repoArgument);
        statusCommand.AddOption(dbDirOption);
        statusCommand.AddOption(storeLocalOption);
        statusCommand.SetHandler((repo, dbDir, storeLocal) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
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
            var needsReindex = lastHash is null || (headHash != "unknown" && !string.Equals(headHash, lastHash, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"Repository:       {repo.FullName}");
            Console.WriteLine($"Database:         {dbPath}");
            Console.WriteLine($"Branch:           {branch}");
            Console.WriteLine($"HEAD:             {headHash}");
            Console.WriteLine($"Last Indexed:     {lastHash ?? "never"}");
            Console.WriteLine($"Needs Re-index:   {(needsReindex ? "yes" : "no")}");
            Console.WriteLine($"Files Indexed:    {fileCount}");
            Console.WriteLine($"Symbols Indexed:  {symbolCount}");
            Console.WriteLine($"References:       {refCount}");
        }, repoArgument, dbDirOption, storeLocalOption);

        // ── clean ──────────────────────────────────────────────────────────────────
        var forceOption = new Option<bool>(new[] { "--force", "-f" }, "Skip confirmation prompt");
        var yesOption   = new Option<bool>(new[] { "--yes", "-y" }, "Skip confirmation prompt");

        var cleanCommand = new Command("clean", "Delete the index database for a repository");
        cleanCommand.AddArgument(repoArgument);
        cleanCommand.AddOption(dbDirOption);
        cleanCommand.AddOption(storeLocalOption);
        cleanCommand.AddOption(forceOption);
        cleanCommand.AddOption(yesOption);
        cleanCommand.SetHandler((repo, dbDir, storeLocal, force, yes) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
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
        }, repoArgument, dbDirOption, storeLocalOption, forceOption, yesOption);

        // ── watch ──────────────────────────────────────────────────────────────────
        var intervalOption = new Option<int>("--interval", () => 30, "Seconds between index checks");

        var watchCommand = new Command("watch", "Continuously re-index on git changes");
        watchCommand.AddArgument(repoArgument);
        watchCommand.AddOption(dbDirOption);
        watchCommand.AddOption(storeLocalOption);
        watchCommand.AddOption(intervalOption);
        watchCommand.SetHandler(async (repo, dbDir, storeLocal, interval) =>
        {
            var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
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
        }, repoArgument, dbDirOption, storeLocalOption, intervalOption);

        // ── root ───────────────────────────────────────────────────────────────────
        var rootCommand = new RootCommand("CodeGraphMcp — local code knowledge graph engine");
        rootCommand.AddCommand(indexCommand);
        rootCommand.AddCommand(queryCommand);
        rootCommand.AddCommand(serveCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(cleanCommand);
        rootCommand.AddCommand(watchCommand);

        return rootCommand;
    }

    private static IReadOnlyList<ILanguageParser> GetParsers() =>
    [
        new CSharpParser(),
        new TypeScriptParser(),
        new HtmlParser(),
        new PowerShellParser()
    ];

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
}
