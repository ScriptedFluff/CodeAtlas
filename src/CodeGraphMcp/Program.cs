using System.CommandLine;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Parser;

var repoArgument = new Argument<DirectoryInfo>("repo-path", "Path to the repository root to index");
var dbDirOption  = new Option<DirectoryInfo?>("--db-dir", "Override directory for the SQLite database");
var storeLocalOption = new Option<bool>(new[] { "--store-local", "-l" }, "Store the database inside the repo under .agent-index/");
var forceFullOption = new Option<bool>("--force-full", "Skip delta detection and re-index everything");

// ── index ──────────────────────────────────────────────────────────────────
var indexCommand = new Command("index", "Parse and index a repository into the knowledge graph");
indexCommand.AddArgument(repoArgument);
indexCommand.AddOption(dbDirOption);
indexCommand.AddOption(storeLocalOption);
indexCommand.AddOption(forceFullOption);
indexCommand.SetHandler(async (repo, dbDir, storeLocal, force) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    using var db = new CodeGraphDb(dbPath);
    db.Migrate();

    if (force || !GitHelpers.IsGitRepo(repo.FullName))
    {
        var orchestrator = new IndexOrchestrator(db);
        var result = await orchestrator.IndexAsync(repo.FullName);
        var headHash = GitHelpers.GetHeadCommitHash(repo.FullName) ?? "unknown";
        var branch = GitHelpers.GetCurrentBranch(repo.FullName) ?? "unknown";
        db.IndexState.SetIndexState(branch, headHash, 0);

        Console.WriteLine($"Indexed {result.FilesProcessed} files, {result.SymbolsWritten} symbols ({result.ElapsedMs}ms) [full]");
    }
    else
    {
        var parsers = GetParsers();
        var delta   = new DeltaEngine(repo.FullName, db, parsers);
        var result  = await delta.RunAsync();

        Console.WriteLine(result.Mode switch
        {
            "noop"  => $"Nothing to index — HEAD unchanged ({result.ElapsedMs}ms) [noop]",
            "full"  => $"Indexed {result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms) [full]",
            "delta" => $"{result.FilesChanged} changed, {result.FilesReindexed} re-indexed, {result.SymbolsAdded} symbols added ({result.ElapsedMs}ms) [delta]",
            _       => $"Done ({result.ElapsedMs}ms)"
        });
    }
}, repoArgument, dbDirOption, storeLocalOption, forceFullOption);

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
var serveRepoArgument     = new Argument<DirectoryInfo>("repo-path", "Repository root to serve");
var serveDbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override SQLite database directory");
var serveStoreLocalOption = new Option<bool>(new[] { "--store-local", "-l" }, "Use .agent-index/ in repo");

var serveCommand = new Command("serve", "Start the MCP stdio server");
serveCommand.AddArgument(serveRepoArgument);
serveCommand.AddOption(serveDbDirOption);
serveCommand.AddOption(serveStoreLocalOption);

serveCommand.SetHandler(async (repo, dbDir, storeLocal) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    await CodeGraphMcp.Mcp.McpServer.RunAsync(repo.FullName, dbPath);
}, serveRepoArgument, serveDbDirOption, serveStoreLocalOption);

// ── root ───────────────────────────────────────────────────────────────────
var rootCommand = new RootCommand("CodeGraphMcp — local code knowledge graph engine");
rootCommand.AddCommand(indexCommand);
rootCommand.AddCommand(queryCommand);
rootCommand.AddCommand(serveCommand);

return await rootCommand.InvokeAsync(args);

static IReadOnlyList<ILanguageParser> GetParsers() =>
[
    new CSharpParser(),
    new TypeScriptParser(),
    new HtmlParser(),
    new PowerShellParser()
];

static string? FindGitRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
