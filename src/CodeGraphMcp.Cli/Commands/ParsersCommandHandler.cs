using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CodeGraphMcp.Plugins;

namespace CodeGraphMcp.Commands;

/// <summary>
/// Handles CLI commands for managing modular drop-in language parser plugins:
/// 'parsers list', 'parsers enable <id>', 'parsers disable <id>', and 'parsers info <id>'.
/// </summary>
public static class ParsersCommandHandler
{
    public static Command CreateParsersCommand()
    {
        var parsersCommand = new Command("parsers", "Manage modular drop-in language parser plugins");

        // ── parsers list ──────────────────────────────────────────────────────────
        var jsonOption = new Option<bool>(new[] { "--json" }, "Output parser list in JSON format");
        var verboseOption = new Option<bool>(new[] { "--verbose", "-v" }, "Show extended details and diagnostics");
        var listCommand = new Command("list", "List all discovered language parsers and their status");
        listCommand.AddAlias("ls");
        listCommand.AddOption(jsonOption);
        listCommand.AddOption(verboseOption);
        listCommand.SetHandler((json, verbose) =>
        {
            ListParsers(json, verbose);
        }, jsonOption, verboseOption);

        // ── parsers enable <id> ───────────────────────────────────────────────────
        var enableIdArg = new Argument<string>("parser-id", "Identifier, language name, or folder name of the parser to enable");
        var enableCommand = new Command("enable", "Enable a disabled language parser");
        enableCommand.AddArgument(enableIdArg);
        enableCommand.SetHandler((id) =>
        {
            EnableParser(id);
        }, enableIdArg);

        // ── parsers disable <id> ──────────────────────────────────────────────────
        var disableIdArg = new Argument<string>("parser-id", "Identifier, language name, or folder name of the parser to disable");
        var disableCommand = new Command("disable", "Disable an active language parser");
        disableCommand.AddArgument(disableIdArg);
        disableCommand.SetHandler((id) =>
        {
            DisableParser(id);
        }, disableIdArg);

        // ── parsers info <id> ─────────────────────────────────────────────────────
        var infoIdArg = new Argument<string>("parser-id", "Identifier or language name of the parser to view");
        var infoJsonOption = new Option<bool>(new[] { "--json" }, "Output parser details in JSON format");
        var infoCommand = new Command("info", "Display detailed metadata and status for a specific parser");
        infoCommand.AddArgument(infoIdArg);
        infoCommand.AddOption(infoJsonOption);
        infoCommand.SetHandler((id, json) =>
        {
            ShowParserInfo(id, json);
        }, infoIdArg, infoJsonOption);

        parsersCommand.AddCommand(listCommand);
        parsersCommand.AddCommand(enableCommand);
        parsersCommand.AddCommand(disableCommand);
        parsersCommand.AddCommand(infoCommand);

        return parsersCommand;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "CLI output serialization.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "CLI output serialization.")]
    public static int ListParsers(bool json = false, bool verbose = false, ParserDirectoryResolver? resolver = null)
    {
        resolver ??= new ParserDirectoryResolver();
        var loader = new DirectoryParserPluginLoader(resolver);
        var result = loader.DiscoverAndLoad();

        if (json)
        {
            var jsonItems = result.Evaluations.Select(e => new ParserListJsonDto(
                Id: e.Candidate.Id,
                Language: e.Candidate.Language,
                Status: e.Status.ToString().ToUpperInvariant(),
                Tier: e.Candidate.SourceTier.ToString(),
                Extensions: e.Candidate.FileExtensions,
                Version: e.Candidate.Version,
                Directory: e.Candidate.DirectoryPath,
                AssemblyPath: e.Candidate.AssemblyPath,
                Reason: e.Reason
            )).ToList();

            var jsonText = JsonSerializer.Serialize(jsonItems, CliJsonContext.Default.ListParserListJsonDto);
            Console.WriteLine(jsonText);
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("CodeGraph Language Parsers");
        Console.WriteLine("==========================");

        var evaluations = result.Evaluations.OrderBy(e => e.Candidate.Id).ToList();

        if (evaluations.Count == 0)
        {
            Console.WriteLine("No language parsers found.");
            Console.WriteLine();
            return 0;
        }

        const int colId = 16;
        const int colLang = 14;
        const int colStatus = 12;
        const int colTier = 8;
        const int colExt = 22;
        const int colVer = 10;

        var header = $"{"ID".PadRight(colId)} {"LANGUAGE".PadRight(colLang)} {"STATUS".PadRight(colStatus)} {"TIER".PadRight(colTier)} {"EXTENSIONS".PadRight(colExt)} {"VERSION".PadRight(colVer)}";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));

        foreach (var eval in evaluations)
        {
            var id = Truncate(eval.Candidate.Id, colId - 1).PadRight(colId);
            var lang = Truncate(eval.Candidate.Language, colLang - 1).PadRight(colLang);
            var statusStr = eval.Status.ToString().ToUpperInvariant();
            var status = Truncate(statusStr, colStatus - 1).PadRight(colStatus);
            var tier = Truncate(eval.Candidate.SourceTier.ToString(), colTier - 1).PadRight(colTier);
            var extStr = string.Join(", ", eval.Candidate.FileExtensions);
            var exts = Truncate(extStr, colExt - 1).PadRight(colExt);
            var ver = Truncate(eval.Candidate.Version ?? "1.0.0", colVer - 1).PadRight(colVer);

            Console.WriteLine($"{id} {lang} {status} {tier} {exts} {ver}");
        }

        Console.WriteLine(new string('-', header.Length));
        Console.WriteLine($"Total: {evaluations.Count} parsers (Active: {evaluations.Count(e => e.Status is ParserRegistrationStatus.Active or ParserRegistrationStatus.ActiveOverride)}, Disabled: {evaluations.Count(e => e.Status == ParserRegistrationStatus.Disabled)}, Blocked: {evaluations.Count(e => e.Status == ParserRegistrationStatus.BlockedShadowing)})");
        Console.WriteLine();

        if (verbose)
        {
            Console.WriteLine("Search Directories:");
            Console.WriteLine($"  User Tier: {resolver.GetUserParsersDirectory()}");
            Console.WriteLine($"  App  Tier: {resolver.GetAppParsersDirectory()}");
            var envDir = resolver.GetEnvParsersDirectory();
            if (envDir != null)
            {
                Console.WriteLine($"  Env  Tier: {envDir}");
            }

            if (result.Diagnostics.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Diagnostics:");
                foreach (var diag in result.Diagnostics)
                {
                    Console.WriteLine($"  {diag}");
                }
            }
            Console.WriteLine();
        }

        return 0;
    }

    public static int EnableParser(string parserId, ParserDirectoryResolver? resolver = null)
    {
        if (string.IsNullOrWhiteSpace(parserId))
        {
            Console.Error.WriteLine("Error: Parser identifier is required.");
            return 1;
        }

        resolver ??= new ParserDirectoryResolver();
        var loader = new DirectoryParserPluginLoader(resolver);
        var result = loader.DiscoverAndLoad();

        // 1. Check if already active
        var activeMatch = result.Evaluations.FirstOrDefault(e =>
            (e.Status is ParserRegistrationStatus.Active or ParserRegistrationStatus.ActiveOverride) &&
            IsMatch(e.Candidate, parserId));

        if (activeMatch != null)
        {
            Console.WriteLine($"Parser '{parserId}' is already active ({activeMatch.Candidate.Language}, {activeMatch.Candidate.SourceTier} tier).");
            return 0;
        }

        // 2. Find disabled candidate
        var disabledMatch = result.Evaluations.FirstOrDefault(e =>
            e.Status == ParserRegistrationStatus.Disabled &&
            IsMatch(e.Candidate, parserId));

        if (disabledMatch == null)
        {
            // Also inspect disabled directories directly by folder name if not loaded by ALC
            var disabledDirs = resolver.GetDisabledSearchDirectories();
            foreach (var (tier, dirPath) in disabledDirs)
            {
                if (!Directory.Exists(dirPath)) continue;

                var subdirs = Directory.GetDirectories(dirPath);
                var matchDir = subdirs.FirstOrDefault(d =>
                    Path.GetFileName(d).Equals(parserId, StringComparison.OrdinalIgnoreCase));

                if (matchDir != null)
                {
                    return MoveToActive(matchDir, tier, resolver, parserId);
                }
            }

            Console.Error.WriteLine($"Error: Disabled parser '{parserId}' was not found in any DisabledParsers directory.");
            return 1;
        }

        var sourceDir = disabledMatch.Candidate.DirectoryPath;
        var sourceTier = disabledMatch.Candidate.SourceTier;
        loader.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return MoveToActive(sourceDir, sourceTier, resolver, parserId);
    }

    public static int DisableParser(string parserId, ParserDirectoryResolver? resolver = null)
    {
        if (string.IsNullOrWhiteSpace(parserId))
        {
            Console.Error.WriteLine("Error: Parser identifier is required.");
            return 1;
        }

        resolver ??= new ParserDirectoryResolver();
        using var loader = new DirectoryParserPluginLoader(resolver);
        var result = loader.DiscoverAndLoad();

        // 1. Check if already disabled
        var disabledMatch = result.Evaluations.FirstOrDefault(e =>
            e.Status == ParserRegistrationStatus.Disabled &&
            IsMatch(e.Candidate, parserId));

        if (disabledMatch != null)
        {
            Console.WriteLine($"Parser '{parserId}' is already disabled ({disabledMatch.Candidate.SourceTier} tier).");
            return 0;
        }

        // 2. Find active or candidate to disable
        var activeMatch = result.Evaluations.FirstOrDefault(e =>
            (e.Status is ParserRegistrationStatus.Active or ParserRegistrationStatus.ActiveOverride or ParserRegistrationStatus.BlockedShadowing or ParserRegistrationStatus.Failed) &&
            IsMatch(e.Candidate, parserId));

        if (activeMatch == null)
        {
            // Also inspect active directories directly by folder name
            var activeDirs = resolver.GetActiveSearchDirectories();
            foreach (var (tier, dirPath) in activeDirs)
            {
                if (!Directory.Exists(dirPath)) continue;

                var subdirs = Directory.GetDirectories(dirPath);
                var matchDir = subdirs.FirstOrDefault(d =>
                    Path.GetFileName(d).Equals(parserId, StringComparison.OrdinalIgnoreCase));

                if (matchDir != null)
                {
                    loader.Dispose();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    return MoveToDisabled(matchDir, tier, resolver, parserId);
                }
            }

            Console.Error.WriteLine($"Error: Active parser '{parserId}' was not found in any Parsers directory.");
            return 1;
        }

        var sourceDir = activeMatch.Candidate.DirectoryPath;
        var sourceTier = activeMatch.Candidate.SourceTier;
        loader.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return MoveToDisabled(sourceDir, sourceTier, resolver, parserId);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "CLI output serialization.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "CLI output serialization.")]
    public static int ShowParserInfo(string parserId, bool json = false, ParserDirectoryResolver? resolver = null)
    {
        if (string.IsNullOrWhiteSpace(parserId))
        {
            Console.Error.WriteLine("Error: Parser identifier is required.");
            return 1;
        }

        resolver ??= new ParserDirectoryResolver();
        var loader = new DirectoryParserPluginLoader(resolver);
        var result = loader.DiscoverAndLoad();

        var match = result.Evaluations.FirstOrDefault(e => IsMatch(e.Candidate, parserId));

        if (match == null)
        {
            Console.Error.WriteLine($"Error: Parser '{parserId}' not found.");
            return 1;
        }

        var c = match.Candidate;

        if (json)
        {
            var infoObj = new ParserInfoJsonDto(
                Id: c.Id,
                Language: c.Language,
                Status: match.Status.ToString().ToUpperInvariant(),
                StatusReason: match.Reason,
                Tier: c.SourceTier.ToString(),
                Version: c.Version,
                Extensions: c.FileExtensions,
                AssemblyPath: c.AssemblyPath,
                DirectoryPath: c.DirectoryPath,
                IsDisabledFolder: c.IsDisabledFolder
            );

            var jsonText = JsonSerializer.Serialize(infoObj, CliJsonContext.Default.ParserInfoJsonDto);
            Console.WriteLine(jsonText);
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Parser: {c.Id}");
        Console.WriteLine(new string('=', c.Id.Length + 8));
        Console.WriteLine($"  Language:        {c.Language}");
        Console.WriteLine($"  Status:          {match.Status.ToString().ToUpperInvariant()}");
        Console.WriteLine($"  Status Reason:   {match.Reason}");
        Console.WriteLine($"  Source Tier:     {c.SourceTier}");
        Console.WriteLine($"  Version:         {c.Version ?? "1.0.0"}");
        Console.WriteLine($"  File Extensions: {string.Join(", ", c.FileExtensions)}");
        Console.WriteLine($"  Directory:       {c.DirectoryPath}");
        Console.WriteLine($"  Assembly:        {c.AssemblyPath}");
        Console.WriteLine();

        return 0;
    }

    private static int MoveToActive(string sourceDir, ParserSourceTier sourceTier, ParserDirectoryResolver resolver, string parserId)
    {
        string targetParentDir = sourceTier switch
        {
            ParserSourceTier.Environment => resolver.GetEnvParsersDirectory() ?? resolver.GetUserParsersDirectory(),
            ParserSourceTier.App => resolver.GetAppParsersDirectory(),
            _ => resolver.GetUserParsersDirectory()
        };

        return MoveFolder(sourceDir, targetParentDir, parserId, enable: true);
    }

    private static int MoveToDisabled(string sourceDir, ParserSourceTier sourceTier, ParserDirectoryResolver resolver, string parserId)
    {
        string targetParentDir = sourceTier switch
        {
            ParserSourceTier.Environment => resolver.GetEnvDisabledParsersDirectory() ?? resolver.GetUserDisabledParsersDirectory(),
            ParserSourceTier.App => resolver.GetAppDisabledParsersDirectory(),
            _ => resolver.GetUserDisabledParsersDirectory()
        };

        return MoveFolder(sourceDir, targetParentDir, parserId, enable: false);
    }

    private static int MoveFolder(string sourceDir, string targetParentDir, string parserId, bool enable)
    {
        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"Error: Source directory '{sourceDir}' does not exist.");
            return 1;
        }

        var folderName = Path.GetFileName(sourceDir);
        var targetDir = Path.Combine(targetParentDir, folderName);

        try
        {
            if (!ParserDirectoryResolver.IsDirectoryWritable(targetParentDir))
            {
                Console.Error.WriteLine($"Error: Permission denied. Cannot write to target directory '{targetParentDir}'.");
                return 1;
            }

            if (!ParserDirectoryResolver.IsDirectoryWritable(sourceDir))
            {
                Console.Error.WriteLine($"Error: Permission denied. Cannot modify source directory '{sourceDir}'.");
                return 1;
            }

            if (Directory.Exists(targetDir))
            {
                // Clean up previous conflicting folder in target
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(targetParentDir);
            Directory.Move(sourceDir, targetDir);

            var action = enable ? "Enabled" : "Disabled";
            Console.WriteLine($"✓ {action} parser '{parserId}'.");
            Console.WriteLine($"  Moved: '{sourceDir}'");
            Console.WriteLine($"  To:    '{targetDir}'");
            return 0;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error: Cannot move parser folder because a file is locked by another process (e.g. running daemon or IDE). Details: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Error: Access denied while moving parser folder. Run with appropriate permissions. Details: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to move parser directory: {ex.Message}");
            return 1;
        }
    }

    private static bool IsMatch(DiscoveredParserCandidate candidate, string query)
    {
        if (candidate.Id.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.Language.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;

        var folderName = Path.GetFileName(candidate.DirectoryPath);
        if (folderName.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;

        var asmName = Path.GetFileNameWithoutExtension(candidate.AssemblyPath);
        if (asmName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            asmName.Equals($"CodeGraphMcp.Parsers.{query}", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
