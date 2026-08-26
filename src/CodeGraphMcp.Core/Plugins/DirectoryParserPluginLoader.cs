using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodeGraphMcp.Parser;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Plugins;

/// <summary>
/// Result of parser plugin discovery and loading across multi-tier directories.
/// </summary>
public sealed record ParserDiscoveryResult(
    IParserRegistry Registry,
    IReadOnlyList<ParserSecurityEvaluation> Evaluations,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Scans multi-tier directories for drop-in language parser plugin assemblies,
/// loads them in isolated <see cref="ParserLoadContext"/> instances, enforces security policies,
/// and registers active parsers into <see cref="IParserRegistry"/>.
/// </summary>
public sealed class DirectoryParserPluginLoader : IDisposable
{
    private readonly ParserDirectoryResolver _directoryResolver;
    private readonly ParserSecuritySettings _securitySettings;
    private readonly ILogger? _logger;
    private readonly List<ParserLoadContext> _loadedContexts = [];

    public DirectoryParserPluginLoader(
        ParserDirectoryResolver? directoryResolver = null,
        ParserSecuritySettings? securitySettings = null,
        ILogger? logger = null)
    {
        _directoryResolver = directoryResolver ?? new ParserDirectoryResolver();
        _securitySettings = securitySettings ?? new ParserSecuritySettings();
        _logger = logger;
    }

    /// <summary>
    /// Discovers and loads all language parser plugins according to security policies.
    /// </summary>
    /// <param name="includeBuiltInFallbacks">Whether to include compiled-in built-in parsers as App-tier baselines.</param>
    public ParserDiscoveryResult DiscoverAndLoad()
    {
        var registry = new ParserRegistry();
        var evaluations = new List<ParserSecurityEvaluation>();
        var diagnostics = new List<string>();
        var activeCandidates = new List<DiscoveredParserCandidate>();

        // 1. Discover and load active parsers from search directories (Env -> User -> App)
        var activeSearchDirs = _directoryResolver.GetActiveSearchDirectories();
        foreach (var (tier, searchPath) in activeSearchDirs)
        {
            if (!Directory.Exists(searchPath)) continue;

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(searchPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"[DirectoryParserPluginLoader] Failed to enumerate '{searchPath}': {ex.Message}");
                continue;
            }

            foreach (var subDir in subDirs)
            {
                LoadParserFromDirectory(subDir, tier, isFolderDisabled: false, registry, evaluations, activeCandidates, diagnostics);
            }
        }

        // 3. Scan disabled parsers for discovery/CLI visibility
        var disabledSearchDirs = _directoryResolver.GetDisabledSearchDirectories();
        foreach (var (tier, searchPath) in disabledSearchDirs)
        {
            if (!Directory.Exists(searchPath)) continue;

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(searchPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"[DirectoryParserPluginLoader] Failed to enumerate disabled directory '{searchPath}': {ex.Message}");
                continue;
            }

            foreach (var subDir in subDirs)
            {
                InspectDisabledDirectory(subDir, tier, evaluations, diagnostics);
            }
        }

        return new ParserDiscoveryResult(registry, evaluations, diagnostics);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic parser loading requires reflection and ALC loading by design.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:TargetRequirements", Justification = "Dynamic parser types are checked for public constructors at runtime.")]
    private void LoadParserFromDirectory(
        string pluginDirectory,
        ParserSourceTier tier,
        bool isFolderDisabled,
        ParserRegistry registry,
        List<ParserSecurityEvaluation> evaluations,
        List<DiscoveredParserCandidate> activeCandidates,
        List<string> diagnostics)
    {
        var dirName = Path.GetFileName(pluginDirectory);
        var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll");
        if (dllFiles.Length == 0) return;

        // Determine primary plugin assembly
        var mainDll = dllFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), dirName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(f), $"CodeGraphMcp.Parsers.{dirName}", StringComparison.OrdinalIgnoreCase))
            ?? dllFiles[0];

        ParserLoadContext? loadContext = null;
        try
        {
            loadContext = new ParserLoadContext(mainDll);
            var asm = loadContext.LoadFromAssemblyPath(mainDll);

            var parserTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ILanguageParser).IsAssignableFrom(t))
                .ToList();

            if (parserTypes.Count == 0)
            {
                diagnostics.Add($"[DirectoryParserPluginLoader] No ILanguageParser implementation found in '{mainDll}'.");
                loadContext.Unload();
                return;
            }

            foreach (var parserType in parserTypes)
            {
                var parserInstance = (ILanguageParser?)Activator.CreateInstance(parserType);
                if (parserInstance == null) continue;

                var candidate = new DiscoveredParserCandidate(
                    Id: parserInstance.Metadata.Id,
                    Language: parserInstance.Language,
                    FileExtensions: parserInstance.FileExtensions,
                    AssemblyPath: mainDll,
                    DirectoryPath: pluginDirectory,
                    SourceTier: tier,
                    IsDisabledFolder: isFolderDisabled,
                    Version: parserInstance.Metadata.Version);

                var evaluation = ParserSecurityEvaluator.Evaluate(candidate, activeCandidates, _securitySettings);
                evaluations.Add(evaluation);

                switch (evaluation.Status)
                {
                    case ParserRegistrationStatus.Active:
                        activeCandidates.Add(candidate);
                        registry.Register(parserInstance);
                        _loadedContexts.Add(loadContext);
                        diagnostics.Add($"[DirectoryParserPluginLoader] Loaded parser '{candidate.Id}' from [{tier}] {mainDll}");
                        break;

                    case ParserRegistrationStatus.ActiveOverride:
                        activeCandidates.RemoveAll(c =>
                            string.Equals(c.Id, candidate.Id, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.Language, candidate.Language, StringComparison.OrdinalIgnoreCase));
                        activeCandidates.Add(candidate);
                        registry.Register(parserInstance);
                        _loadedContexts.Add(loadContext);
                        diagnostics.Add($"[DirectoryParserPluginLoader] Override parser '{candidate.Id}' accepted from [{tier}] {mainDll}");
                        break;

                    case ParserRegistrationStatus.BlockedShadowing:
                        diagnostics.Add($"[DirectoryParserPluginLoader] Security Block: {evaluation.Reason}");
                        loadContext.Unload();
                        break;

                    case ParserRegistrationStatus.Disabled:
                        loadContext.Unload();
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            var failedCandidate = new DiscoveredParserCandidate(
                Id: dirName,
                Language: dirName,
                FileExtensions: [],
                AssemblyPath: mainDll,
                DirectoryPath: pluginDirectory,
                SourceTier: tier,
                IsDisabledFolder: isFolderDisabled);

            evaluations.Add(new ParserSecurityEvaluation(failedCandidate, ParserRegistrationStatus.Failed, ex.Message));
            diagnostics.Add($"[DirectoryParserPluginLoader] Error loading plugin from '{mainDll}': {ex.Message}");

            try { loadContext?.Unload(); } catch { }
        }
    }

    private static void InspectDisabledDirectory(
        string pluginDirectory,
        ParserSourceTier tier,
        List<ParserSecurityEvaluation> evaluations,
        List<string> diagnostics)
    {
        var dirName = Path.GetFileName(pluginDirectory);
        var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll");
        var mainDll = dllFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), dirName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(f), $"CodeGraphMcp.Parsers.{dirName}", StringComparison.OrdinalIgnoreCase))
            ?? dllFiles.FirstOrDefault() ?? Path.Combine(pluginDirectory, $"{dirName}.dll");

        var candidate = new DiscoveredParserCandidate(
            Id: dirName.ToLowerInvariant(),
            Language: dirName.ToLowerInvariant(),
            FileExtensions: [],
            AssemblyPath: mainDll,
            DirectoryPath: pluginDirectory,
            SourceTier: tier,
            IsDisabledFolder: true);

        evaluations.Add(new ParserSecurityEvaluation(candidate, ParserRegistrationStatus.Disabled, "Parser is located in a DisabledParsers folder"));
    }

    /// <summary>
    /// Unloads all dynamic plugin contexts.
    /// </summary>
    public void Dispose()
    {
        foreach (var context in _loadedContexts)
        {
            try
            {
                context.Unload();
            }
            catch { }
        }
        _loadedContexts.Clear();
    }
}
