using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Git;

/// <summary>
/// Parses uncommitted (dirty) files on demand and caches the results in memory.
/// Results overlay the SQLite DB — dirty symbols take precedence.
/// Thread-safe for concurrent read access; refresh is single-threaded.
/// </summary>
public sealed class DirtyFileBuffer(string repoPath, IReadOnlyList<ILanguageParser> parsers)
{
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codegraph", "bin", "obj", "publish",
        "node_modules", "dist", ".next", "coverage", ".vs"
    };

    private volatile IReadOnlyDictionary<string, IReadOnlyList<Symbol>> _cache
        = new Dictionary<string, IReadOnlyList<Symbol>>();

    private readonly Dictionary<string, ILanguageParser> _extensionMap = BuildMap(parsers);
    private readonly object _refreshLock = new();

    /// <summary>
    /// Refreshes the buffer by querying git status and re-parsing all dirty files.
    /// Safe to call from any thread; only one refresh runs at a time.
    /// </summary>
    public void Refresh()
    {
        lock (_refreshLock)
        {
            var dirtyFiles = GitHelpers.GetDirtyFiles(repoPath);
            var newCache   = new Dictionary<string, IReadOnlyList<Symbol>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var relPath in dirtyFiles)
            {
                if (IsIgnoredPath(relPath)) continue;

                var ext = Path.GetExtension(relPath);
                if (!_extensionMap.TryGetValue(ext, out var parser)) continue;

                var fullPath = Path.Combine(repoPath, relPath);
                if (!File.Exists(fullPath)) continue;

                try
                {
                    var source   = File.ReadAllText(fullPath);
                    var normPath = relPath.Replace('\\', '/');
                    var symbols  = parser.Parse(normPath, source).ToList();
                    newCache[normPath] = symbols;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DirtyBuffer] Warning: failed to parse {relPath}: {ex.Message}");
                }
            }

            _cache = newCache;
        }
    }

    /// <summary>
    /// Updates or invalidates a single file in the buffer.
    /// Prepared for FileSystemWatcher / live MCP session events.
    /// </summary>
    public void UpdateFile(string relFilePath)
    {
        lock (_refreshLock)
        {
            var normPath = relFilePath.Replace('\\', '/');
            if (IsIgnoredPath(normPath)) return;

            var ext = Path.GetExtension(normPath);
            if (!_extensionMap.TryGetValue(ext, out var parser)) return;

            var fullPath = Path.Combine(repoPath, relFilePath);
            var newCache = new Dictionary<string, IReadOnlyList<Symbol>>(_cache, StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(fullPath))
            {
                newCache.Remove(normPath);
            }
            else
            {
                try
                {
                    var source  = File.ReadAllText(fullPath);
                    var symbols = parser.Parse(normPath, source).ToList();
                    newCache[normPath] = symbols;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DirtyBuffer] Warning: failed to parse {relFilePath}: {ex.Message}");
                }
            }

            _cache = newCache;
        }
    }

    /// <summary>
    /// Removes a file from the dirty buffer (e.g. after a git commit or revert).
    /// </summary>
    public void Invalidate(string relFilePath)
    {
        lock (_refreshLock)
        {
            var normPath = relFilePath.Replace('\\', '/');
            if (_cache.ContainsKey(normPath))
            {
                var newCache = new Dictionary<string, IReadOnlyList<Symbol>>(_cache, StringComparer.OrdinalIgnoreCase);
                newCache.Remove(normPath);
                _cache = newCache;
            }
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => IgnoredDirs.Contains(p));
    }

    /// <summary>
    /// Returns live symbols for the given relative file path if the file is dirty,
    /// or null if the file has no dirty state (fall back to SQLite).
    /// </summary>
    public IReadOnlyList<Symbol>? GetDirtySymbols(string relFilePath)
    {
        var norm = relFilePath.Replace('\\', '/');
        return _cache.TryGetValue(norm, out var syms) ? syms : null;
    }

    /// <summary>
    /// Searches all dirty symbols by name, optionally filtered by language.
    /// Returns null if the buffer has no dirty state matching the name.
    /// </summary>
    public IReadOnlyList<Symbol>? FindByName(string symbolName, string? language = null)
    {
        var allDirty = _cache.Values
            .SelectMany(s => s)
            .Where(s => s.SymbolName.Contains(symbolName, StringComparison.OrdinalIgnoreCase))
            .Where(s => language is null || string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => string.Equals(s.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return allDirty.Count > 0 ? allDirty : null;
    }

    /// <summary>Total number of files currently tracked as dirty.</summary>
    public int DirtyFileCount => _cache.Count;

    /// <summary>
    /// Returns all symbols across all dirty files — used for wildcard search.
    /// </summary>
    public IEnumerable<Symbol> GetAllDirtySymbols() =>
        _cache.Values.SelectMany(s => s);

    private static Dictionary<string, ILanguageParser> BuildMap(IReadOnlyList<ILanguageParser> parsers)
    {
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
    }
}
