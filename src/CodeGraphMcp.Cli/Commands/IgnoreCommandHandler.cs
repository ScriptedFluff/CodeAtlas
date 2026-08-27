using System.CommandLine;
using System.Text.Json;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Indexer;

namespace CodeGraphMcp.Commands;

/// <summary>
/// Handles CLI commands for managing project ignore rules:
/// 'ignore add <pattern> [--purge]', 'ignore list', and 'ignore remove <pattern>'.
/// </summary>
public static class IgnoreCommandHandler
{
    public static Command CreateIgnoreCommand()
    {
        var ignoreCommand = new Command("ignore", "Manage project-level file and directory ignore patterns in .codegraph/settings.json");

        var repoOption = new Option<DirectoryInfo?>(new[] { "--repo", "-r", "--root" }, "Repository path (defaults to current working directory or git root)");

        // ── ignore add <pattern> ──────────────────────────────────────────────
        var addPatternArg = new Argument<string>("pattern", "Glob pattern or file path to ignore (e.g. 'tests/fixtures/**', '*.generated.cs')");
        var purgeOption = new Option<bool>(new[] { "--purge", "-p" }, "Immediately purge indexed files, symbols, and references matching this pattern from the database");
        var addCommand = new Command("add", "Add a pattern to the project ignore configuration");
        addCommand.AddArgument(addPatternArg);
        addCommand.AddOption(repoOption);
        addCommand.AddOption(purgeOption);
        addCommand.SetHandler(async (pattern, repo, purge) =>
        {
            var repoRoot = ResolveRepoRoot(repo);
            var result = ProjectConfigManager.AddIgnorePattern(repoRoot, pattern);
            Console.WriteLine(result.Message);

            if (purge && (result.Changed || result.AlreadyExisted))
            {
                var dbPath = StorageConfig.ResolveDbPath(null, repoRoot);
                if (File.Exists(dbPath))
                {
                    using var db = new CodeGraphDb(dbPath);
                    db.Migrate();
                    var globIndexer = new GlobFileIndexer();
                    var purgeRes = await globIndexer.PurgeGlobAsync(repoRoot, db, pattern);
                    if (purgeRes.FilesDeleted > 0)
                    {
                        Console.WriteLine($"Purged {purgeRes.FilesDeleted} indexed files ({purgeRes.SymbolsDeleted} symbols, {purgeRes.ReferencesDeleted} references) matching '{pattern}'.");
                    }
                    else
                    {
                        Console.WriteLine($"No active indexed files in graph matched '{pattern}'.");
                    }
                }
            }
        }, addPatternArg, repoOption, purgeOption);

        // ── ignore list ───────────────────────────────────────────────────────
        var jsonOption = new Option<bool>(new[] { "--json" }, "Output ignore patterns in JSON format");
        var listCommand = new Command("list", "List all active ignore patterns in the project configuration");
        listCommand.AddAlias("ls");
        listCommand.AddOption(repoOption);
        listCommand.AddOption(jsonOption);
        listCommand.SetHandler((repo, json) =>
        {
            var repoRoot = ResolveRepoRoot(repo);
            var patterns = ProjectConfigManager.GetIgnorePatterns(repoRoot);
            var settingsPath = ProjectConfigManager.GetSettingsPath(repoRoot);

            if (json)
            {
                var dto = new IgnoreListJsonDto(repoRoot, settingsPath, patterns);
                var output = JsonSerializer.Serialize(dto, CliJsonContext.Default.IgnoreListJsonDto);
                Console.WriteLine(output);
                return;
            }

            if (patterns.Count == 0)
            {
                Console.WriteLine($"No custom ignore patterns configured in .codegraph/settings.json ({repoRoot})");
                return;
            }

            Console.WriteLine($"Active Ignore Patterns ({patterns.Count}):");
            foreach (var p in patterns)
            {
                Console.WriteLine($"  - {p}");
            }
        }, repoOption, jsonOption);

        // ── ignore remove <pattern> ───────────────────────────────────────────
        var removePatternArg = new Argument<string>("pattern", "Glob pattern or file path to remove from ignore configuration");
        var removeCommand = new Command("remove", "Remove an ignore pattern from the project configuration");
        removeCommand.AddAlias("rm");
        removeCommand.AddArgument(removePatternArg);
        removeCommand.AddOption(repoOption);
        removeCommand.SetHandler((pattern, repo) =>
        {
            var repoRoot = ResolveRepoRoot(repo);
            var result = ProjectConfigManager.RemoveIgnorePattern(repoRoot, pattern);
            Console.WriteLine(result.Message);
        }, removePatternArg, repoOption);

        ignoreCommand.AddCommand(addCommand);
        ignoreCommand.AddCommand(listCommand);
        ignoreCommand.AddCommand(removeCommand);

        return ignoreCommand;
    }

    private static string ResolveRepoRoot(DirectoryInfo? repoOpt)
    {
        var startDir = repoOpt?.FullName ?? Directory.GetCurrentDirectory();
        return repoOpt is not null ? startDir : (FindGitRoot(startDir) ?? startDir);
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
}
