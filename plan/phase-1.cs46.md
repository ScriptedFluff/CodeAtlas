# Phase 1 — Core Engine & Tree-sitter Parsers
## Detailed Implementation Plan — CodeAtlas (C# / .NET 9)

**Version:** 1.0.0  
**Phase:** 1 of 4  
**Timeline:** Weeks 1–2  
**Estimated Effort:** 52 hours  
**Tasks:** ENG-01 · ENG-02 · ENG-03  
**Status:** Ready for Execution

---

## Objective

Produce a working, AOT-compiled CLI binary (`CodeAtlas.exe`) that can:

1. Accept a repository path as input
2. Walk the directory tree, dispatch files to language-specific Tree-sitter parsers
3. Extract symbols (classes, interfaces, methods, properties) with precise line/byte ranges
4. Persist everything into a versioned SQLite knowledge graph
5. Survive a `dotnet publish -r win-x64 -p:PublishAot=true` build without trim warnings

No MCP, no git delta engine, no live file buffer — those belong to Phases 2 and 3.  
Phase 1 is the bedrock every later phase depends on. Get this right and the rest is additive.

---

## Phase 1 Exit Criteria (Non-Negotiable)

- [ ] `dotnet publish -r win-x64 -p:PublishAot=true` produces a single working executable — no runtime errors
- [ ] `.\CodeAtlas.exe index D:\Projects\C#\repositorydb --store-local` completes without exceptions
- [ ] SQLite `symbols` table contains correct symbol names, types, file paths, and line numbers — verified by manual spot check
- [ ] Indexing the same repo twice produces identical row counts (upsert idempotency)
- [ ] `.\CodeAtlas.exe query <SymbolName>` returns a formatted result from the DB
- [ ] No `NullReferenceException` escapes to the console — all parser errors are caught, logged, and skipped

---

## Directory Structure (Phase 1 Scope)

```
D:\Projects\C#\repositorydb\
├── CodeAtlas.sln
├── src\
│   └── CodeAtlas\
│       ├── CodeAtlas.csproj            ← AOT-configured project file
│       ├── Program.cs                     ← CLI entry point (System.CommandLine)
│       ├── Config\
│       │   └── StorageConfig.cs           ← DB path resolution (hash, flags, env)
│       ├── Models\
│       │   ├── Symbol.cs                  ← Strongly-typed record for AST symbols
│       │   ├── SymbolType.cs              ← Enum: Class, Interface, Method, etc.
│       │   └── IndexResult.cs             ← Summary returned after indexing
│       ├── Db\
│       │   ├── CodeAtlasDb.cs             ← Connection factory + migration runner
│       │   ├── Migrations\
│       │   │   ├── M001_InitialSchema.cs  ← All 5 tables
│       │   │   └── M002_Indices.cs        ← Performance indices
│       │   └── Queries\
│       │       ├── SymbolQueries.cs       ← CRUD for symbols + namespaces
│       │       └── ReferenceQueries.cs    ← Placeholder (populated in Phase 3)
│       ├── Parser\
│       │   ├── ILanguageParser.cs         ← Parser contract
│       │   ├── CSharpParser.cs            ← Full implementation
│       │   ├── TypeScriptParser.cs        ← Full implementation
│       │   ├── HtmlParser.cs              ← Stub (captures none — records file only)
│       │   └── PowerShellParser.cs        ← Stub (captures none — records file only)
│       └── Indexer\
│           └── IndexOrchestrator.cs       ← Directory walk + dispatch + batch write
├── tests\
│   └── CodeAtlas.Tests\
│       ├── CodeAtlas.Tests.csproj
│       ├── Fixtures\
│       │   ├── SampleClass.cs             ← Known symbols for assertion
│       │   └── SampleComponent.ts         ← Known symbols for assertion
│       └── ParserTests.cs
└── plan\
    ├── implementation-plan.cs46.md
    └── phase-1.cs46.md                    ← This file
```

> **Note on HTML & PowerShell parsers:** Stubs are created now so `IndexOrchestrator` can register all 4 languages from day 1. Full Tree-sitter query implementations for PS1 and HTML are Phase 1 stretch goals — prioritise correctness of C# and TypeScript first, since those have the highest symbol density in this codebase.

---

## ENG-01 — Project Scaffold & CLI (12 hours)

### 1.1 — Solution & Project Creation

```powershell
# Create solution
dotnet new sln -n CodeAtlas -o D:\Projects\C#\repositorydb

# Create main project
dotnet new console -n CodeAtlas -o D:\Projects\C#\repositorydb\src\CodeAtlas
dotnet sln D:\Projects\C#\repositorydb\CodeAtlas.sln add `
    D:\Projects\C#\repositorydb\src\CodeAtlas\CodeAtlas.csproj

# Create test project
dotnet new xunit -n CodeAtlas.Tests -o D:\Projects\C#\repositorydb\tests\CodeAtlas.Tests
dotnet sln D:\Projects\C#\repositorydb\CodeAtlas.sln add `
    D:\Projects\C#\repositorydb\tests\CodeAtlas.Tests\CodeAtlas.Tests.csproj
```

### 1.2 — NuGet Packages

Add to `src\CodeAtlas\CodeAtlas.csproj`:

```powershell
cd D:\Projects\C#\repositorydb\src\CodeAtlas

dotnet add package System.CommandLine --prerelease
dotnet add package Microsoft.Data.Sqlite
dotnet add package SimpleMigrations
dotnet add package TreeSitter
dotnet add package TreeSitter.Language.CSharp
dotnet add package TreeSitter.Language.TypeScript
dotnet add package TreeSitter.Language.Html
dotnet add package TreeSitter.Language.PowerShell
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Serilog.Extensions.Hosting
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

Add to `tests\CodeAtlas.Tests\CodeAtlas.Tests.csproj`:

```powershell
cd D:\Projects\C#\repositorydb\tests\CodeAtlas.Tests

dotnet add reference ..\..\src\CodeAtlas\CodeAtlas.csproj
dotnet add package Microsoft.Data.Sqlite
```

### 1.3 — Project File Configuration

`CodeAtlas.csproj` must be AOT-ready from the start. Catching trim issues late is painful.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Native AOT -->
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>true</StripSymbols>

    <!-- Trim analysis -->
    <TrimmerRootAssembly Include="CodeAtlas" />
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  </PropertyGroup>

</Project>
```

> **AOT Gotcha:** `Microsoft.Data.Sqlite` relies on SQLitePCLRaw which uses P/Invoke — this is AOT-compatible but requires the native `e_sqlite3.dll` to be bundled alongside the executable. Confirm it appears in the publish output directory before moving forward.

### 1.4 — CLI Entry Point (`Program.cs`)

The CLI must support three subcommands. Only `index` and `query` are wired in Phase 1; `serve` is a placeholder:

```csharp
using System.CommandLine;
using CodeAtlas.Core.Config;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Indexer;
using Microsoft.Extensions.Logging;

var repoArgument = new Argument<DirectoryInfo>("repo-path", "Path to the repository root to index");
var dbDirOption  = new Option<DirectoryInfo?>("--db-dir", "Override directory for the SQLite database");
var storeLocalOption = new Option<bool>("--store-local", "-l", "Store the database inside the repo under .agent-index/");

// ── index ──────────────────────────────────────────────────────────────────
var indexCommand = new Command("index", "Parse and index a repository into the knowledge graph");
indexCommand.AddArgument(repoArgument);
indexCommand.AddOption(dbDirOption);
indexCommand.AddOption(storeLocalOption);
indexCommand.SetHandler(async (repo, dbDir, storeLocal) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    using var db = new CodeAtlasDb(dbPath);
    db.Migrate();
    var orchestrator = new IndexOrchestrator(db);
    var result = await orchestrator.IndexAsync(repo.FullName);
    Console.WriteLine($"Indexed {result.FilesProcessed} files, {result.SymbolsWritten} symbols ({result.ElapsedMs}ms)");
}, repoArgument, dbDirOption, storeLocalOption);

// ── query ──────────────────────────────────────────────────────────────────
var symbolArgument = new Argument<string>("symbol-name", "Name of the symbol to look up");
var queryCommand = new Command("query", "Query the knowledge graph for a symbol");
queryCommand.AddArgument(symbolArgument);
queryCommand.AddOption(dbDirOption);
queryCommand.AddOption(storeLocalOption);
queryCommand.SetHandler((symbol, dbDir, storeLocal) =>
{
    // Require --store-local or --db-dir so query knows which DB to open.
    // For now, require --store-local as default during Phase 1 testing.
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, Directory.GetCurrentDirectory());
    using var db = new CodeAtlasDb(dbPath);
    db.Migrate();
    var results = db.Symbols.FindByName(symbol);
    foreach (var sym in results)
        Console.WriteLine($"[{sym.SymbolType}] {sym.SymbolName}  {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
}, symbolArgument, dbDirOption, storeLocalOption);

// ── serve ──────────────────────────────────────────────────────────────────
var serveCommand = new Command("serve", "Start the MCP server (Phase 3)");
serveCommand.SetHandler(() => Console.WriteLine("MCP server is not yet implemented (Phase 3)."));

// ── root ───────────────────────────────────────────────────────────────────
var rootCommand = new RootCommand("CodeAtlas — local code knowledge graph engine");
rootCommand.AddCommand(indexCommand);
rootCommand.AddCommand(queryCommand);
rootCommand.AddCommand(serveCommand);

return await rootCommand.InvokeAsync(args);
```

### 1.5 — Storage Config (`Config/StorageConfig.cs`)

```csharp
using System.Security.Cryptography;
using System.Text;

namespace CodeAtlas.Core.Config;

public static class StorageConfig
{
    /// <summary>
    /// Resolves the SQLite database file path using a strict priority chain:
    ///   1. --db-dir &lt;path&gt; (explicit override)
    ///   2. --store-local  (.agent-index/ inside the repo)
    ///   3. Default: %LOCALAPPDATA%\CodeGraph\dbs\&lt;hash&gt;.db
    /// </summary>
    public static string ResolveDbPath(string? dbDir, bool storeLocal, string repoPath)
    {
        // Normalise so the hash is stable regardless of trailing slashes
        var normalisedRepo = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar);

        if (dbDir is not null)
        {
            Directory.CreateDirectory(dbDir);
            return Path.Combine(dbDir, $"{HashPath(normalisedRepo)}.db");
        }

        if (storeLocal)
        {
            var localDir = Path.Combine(normalisedRepo, ".agent-index");
            Directory.CreateDirectory(localDir);
            AppendGitIgnore(normalisedRepo);
            return Path.Combine(localDir, "atlas.db");
        }

        // Default: global LOCALAPPDATA store
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var globalDir = Path.Combine(appData, "CodeGraph", "dbs");
        Directory.CreateDirectory(globalDir);
        return Path.Combine(globalDir, $"{HashPath(normalisedRepo)}.db");
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    private static void AppendGitIgnore(string repoPath)
    {
        var gitIgnorePath = Path.Combine(repoPath, ".gitignore");
        const string entry = "\n# CodeAtlas local index\n.agent-index/\n";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(gitIgnorePath, entry);
            return;
        }

        var content = File.ReadAllText(gitIgnorePath);
        if (!content.Contains(".agent-index/"))
            File.AppendAllText(gitIgnorePath, entry);
    }
}
```

### 1.6 — ENG-01 Stability Gate

```powershell
dotnet run --project src\CodeAtlas -- index --help
# Expected: prints usage with <repo-path>, --db-dir, --store-local

dotnet publish src\CodeAtlas -r win-x64 -p:PublishAot=true -p:StripSymbols=true -o publish\
# Expected: publish\ contains CodeAtlas.exe (~10-30MB) and e_sqlite3.dll
```

---

## ENG-02 — Tree-sitter Parser Integration (24 hours)

### 2.1 — Models

**`Models/SymbolType.cs`**
```csharp
namespace CodeAtlas.Models;

public enum SymbolType
{
    Class,
    Interface,
    Struct,
    Enum,
    Method,
    Property,
    Field,
    Function,       // TypeScript top-level functions
    TypeAlias,      // TypeScript type X = ...
    Variable,       // TypeScript const/let exports
}
```

**`Models/Symbol.cs`**
```csharp
namespace CodeAtlas.Models;

public sealed record Symbol
{
    public required string SymbolName { get; init; }
    public required SymbolType SymbolType { get; init; }
    public string? ClassName { get; init; }         // Enclosing class (null for top-level)
    public string? Namespace { get; init; }         // Resolved namespace/module
    public string? Language { get; init; }          // "csharp" | "typescript" | "html" | "powershell"
    public required string FilePath { get; init; }  // Relative to repo root
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int StartByte { get; init; }
    public required int EndByte { get; init; }
    public string? Signature { get; init; }         // Return type + parameters
    public string? DocComment { get; init; }        // XML doc summary (C#) or JSDoc (TS)
}
```

**`Models/IndexResult.cs`**
```csharp
namespace CodeAtlas.Models;

public sealed record IndexResult(int FilesProcessed, int SymbolsWritten, long ElapsedMs);
```

### 2.2 — Parser Interface

**`Parser/ILanguageParser.cs`**
```csharp
using CodeAtlas.Models;

namespace CodeAtlas.Parser;

public interface ILanguageParser
{
    /// <summary>File extensions this parser handles, e.g. [".cs"]</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Language identifier stored in the DB, e.g. "csharp"</summary>
    string Language { get; }

    /// <summary>
    /// Parse the source file and yield extracted symbols.
    /// Implementation MUST NOT throw — catch internally and yield nothing on failure.
    /// </summary>
    IEnumerable<Symbol> Parse(string filePath, string source);
}
```

> **Contract Note:** Parsers must swallow internal failures. `IndexOrchestrator` calls parsers inside a `try/catch` as a backstop, but individual parsers should log and continue gracefully at the symbol level too — a bad method body should not drop the entire file.

### 2.3 — C# Parser (`Parser/CSharpParser.cs`)

The C# parser uses Tree-sitter S-expression queries to capture declarations. The query captures are named by convention (`@class.def`, `@method.name`, etc.) so the extraction loop can discriminate by capture name prefix.

```csharp
using CodeAtlas.Models;
using TreeSitter;
using TreeSitter.Language.CSharp;

namespace CodeAtlas.Parser;

public sealed class CSharpParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".cs"];
    public string Language => "csharp";

    // S-expression queries targeting the C# grammar node types.
    // Each capture group is prefixed with the symbol type for easy dispatch.
    private const string Query = """
        (class_declaration
            name: (identifier) @class.name) @class.def

        (interface_declaration
            name: (identifier) @interface.name) @interface.def

        (struct_declaration
            name: (identifier) @struct.name) @struct.def

        (enum_declaration
            name: (identifier) @enum.name) @enum.def

        (method_declaration
            name: (identifier) @method.name) @method.def

        (property_declaration
            name: (identifier) @property.name) @property.def
        """;

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        using var parser = new global::TreeSitter.Parser();
        parser.SetLanguage(CSharp.Language);
        using var tree = parser.Parse(source);
        var query = new Query(CSharp.Language, Query);

        string? currentClass = null;
        string? currentNamespace = null;

        foreach (var match in query.Matches(tree.RootNode))
        {
            Symbol? symbol = null;

            foreach (var capture in match.Captures)
            {
                var captureName = query.CaptureNames[capture.Index];

                if (captureName.EndsWith(".def"))
                {
                    var kind = captureName.Replace(".def", "");
                    var node = capture.Node;
                    var nameNode = match.Captures
                        .FirstOrDefault(c => query.CaptureNames[c.Index] == $"{kind}.name")
                        .Node;

                    if (nameNode is null) continue;

                    var symbolType = kind switch
                    {
                        "class"     => SymbolType.Class,
                        "interface" => SymbolType.Interface,
                        "struct"    => SymbolType.Struct,
                        "enum"      => SymbolType.Enum,
                        "method"    => SymbolType.Method,
                        "property"  => SymbolType.Property,
                        _           => SymbolType.Class
                    };

                    // Track enclosing class for methods/properties
                    if (symbolType is SymbolType.Class or SymbolType.Interface or SymbolType.Struct)
                        currentClass = nameNode.Utf8Text(source);

                    symbol = new Symbol
                    {
                        SymbolName  = nameNode.Utf8Text(source),
                        SymbolType  = symbolType,
                        ClassName   = symbolType is SymbolType.Method or SymbolType.Property ? currentClass : null,
                        Namespace   = currentNamespace,
                        Language    = Language,
                        FilePath    = filePath,
                        StartLine   = (int)node.StartPosition.Row + 1,
                        EndLine     = (int)node.EndPosition.Row + 1,
                        StartByte   = (int)node.StartByte,
                        EndByte     = (int)node.EndByte,
                        Signature   = ExtractSignature(node, source, symbolType),
                        DocComment  = ExtractDocComment(node, source),
                    };
                }
            }

            if (symbol is not null)
                yield return symbol;
        }
    }

    private static string? ExtractSignature(Node node, string source, SymbolType type)
    {
        // For methods: extract return type + parameter list text
        if (type is not SymbolType.Method) return null;

        var returnType = node.ChildByFieldName("type")?.Utf8Text(source);
        var parameters = node.ChildByFieldName("parameter_list")?.Utf8Text(source);
        return returnType is null ? null : $"{returnType} {parameters}".Trim();
    }

    private static string? ExtractDocComment(Node node, string source)
    {
        // Look for preceding sibling comment nodes (/// lines)
        var prev = node.PrevNamedSibling;
        if (prev is null || prev.Type != "comment") return null;
        return prev.Utf8Text(source).TrimStart('/', ' ');
    }
}
```

### 2.4 — TypeScript Parser (`Parser/TypeScriptParser.cs`)

```csharp
using CodeAtlas.Models;
using TreeSitter;
using TreeSitter.Language.TypeScript;

namespace CodeAtlas.Parser;

public sealed class TypeScriptParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".ts", ".tsx"];
    public string Language => "typescript";

    private const string Query = """
        (interface_declaration
            name: (type_identifier) @interface.name) @interface.def

        (class_declaration
            name: (type_identifier) @class.name) @class.def

        (function_declaration
            name: (identifier) @function.name) @function.def

        (method_definition
            name: (property_identifier) @method.name) @method.def

        (type_alias_declaration
            name: (type_identifier) @typealias.name) @typealias.def
        """;

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        using var parser = new global::TreeSitter.Parser();
        parser.SetLanguage(TypeScript.Language);
        using var tree = parser.Parse(source);
        var query = new Query(TypeScript.Language, Query);

        foreach (var match in query.Matches(tree.RootNode))
        {
            foreach (var capture in match.Captures)
            {
                var captureName = query.CaptureNames[capture.Index];
                if (!captureName.EndsWith(".def")) continue;

                var kind = captureName.Replace(".def", "");
                var defNode = capture.Node;
                var nameCapture = match.Captures
                    .FirstOrDefault(c => query.CaptureNames[c.Index] == $"{kind}.name");

                if (nameCapture.Node is null) continue;

                var symbolType = kind switch
                {
                    "interface" => SymbolType.Interface,
                    "class"     => SymbolType.Class,
                    "function"  => SymbolType.Function,
                    "method"    => SymbolType.Method,
                    "typealias" => SymbolType.TypeAlias,
                    _           => SymbolType.Class
                };

                yield return new Symbol
                {
                    SymbolName = nameCapture.Node.Utf8Text(source),
                    SymbolType = symbolType,
                    Language   = Language,
                    FilePath   = filePath,
                    StartLine  = (int)defNode.StartPosition.Row + 1,
                    EndLine    = (int)defNode.EndPosition.Row + 1,
                    StartByte  = (int)defNode.StartByte,
                    EndByte    = (int)defNode.EndByte,
                };
            }
        }
    }
}
```

### 2.5 — HTML & PowerShell Stubs

These stubs allow `IndexOrchestrator` to register all 4 languages without crashing. They do not emit symbols — the files are recorded in the walk but skipped. Full implementations are Phase 1 stretch goals.

```csharp
// Parser/HtmlParser.cs
using CodeAtlas.Models;
namespace CodeAtlas.Parser;

public sealed class HtmlParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".html", ".htm"];
    public string Language => "html";
    public IEnumerable<Symbol> Parse(string filePath, string source) => [];
}

// Parser/PowerShellParser.cs
using CodeAtlas.Models;
namespace CodeAtlas.Parser;

public sealed class PowerShellParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".ps1", ".psm1", ".psd1"];
    public string Language => "powershell";
    public IEnumerable<Symbol> Parse(string filePath, string source) => [];
}
```

### 2.6 — Index Orchestrator (`Indexer/IndexOrchestrator.cs`)

The orchestrator is the integration point. It holds the list of parsers, walks the directory tree, dispatches by extension, and writes symbols in batches.

```csharp
using System.Diagnostics;
using CodeAtlas.Core.Db;
using CodeAtlas.Models;
using CodeAtlas.Parser;
using Microsoft.Extensions.Logging;

namespace CodeAtlas.Core.Indexer;

public sealed class IndexOrchestrator(CodeAtlasDb db, ILogger<IndexOrchestrator>? logger = null)
{
    // Directories to skip — treat as a lookup set for O(1) check
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".agent-index", "bin", "obj",
        "node_modules", "dist", ".next", "coverage", ".vs"
    };

    private readonly IReadOnlyList<ILanguageParser> _parsers =
    [
        new CSharpParser(),
        new TypeScriptParser(),
        new HtmlParser(),
        new PowerShellParser(),
    ];

    // Build a fast extension → parser lookup once
    private readonly Dictionary<string, ILanguageParser> _extensionMap = BuildExtensionMap();

    private static Dictionary<string, ILanguageParser> BuildExtensionMap()
    {
        IReadOnlyList<ILanguageParser> parsers =
        [
            new CSharpParser(),
            new TypeScriptParser(),
            new HtmlParser(),
            new PowerShellParser(),
        ];
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
    }

    public async Task<IndexResult> IndexAsync(string repoPath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var files = EnumerateFiles(repoPath).ToList();

        logger?.LogInformation("Found {FileCount} files to process in {RepoPath}", files.Count, repoPath);

        int symbolsWritten = 0;

        // Process files in parallel batches (parser is CPU-bound)
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, async (filePath, ct) =>
        {
            var ext = Path.GetExtension(filePath);
            if (!_extensionMap.TryGetValue(ext, out var parser)) return;

            try
            {
                var source = await File.ReadAllTextAsync(filePath, ct);
                var relPath = Path.GetRelativePath(repoPath, filePath);
                var symbols = parser.Parse(relPath, source).ToList();

                // Writes are serialised through the DB layer (SQLite is single-writer)
                lock (db)
                {
                    db.Symbols.UpsertFile(relPath, symbols);
                    Interlocked.Add(ref symbolsWritten, symbols.Count);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse {FilePath} — skipping", filePath);
            }
        });

        sw.Stop();
        logger?.LogInformation("Indexed {Symbols} symbols in {Ms}ms", symbolsWritten, sw.ElapsedMilliseconds);
        return new IndexResult(files.Count, symbolsWritten, sw.ElapsedMilliseconds);
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (!IgnoredDirs.Contains(Path.GetFileName(sub)))
                    queue.Enqueue(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (_extensionMap.ContainsKey(ext))
                    yield return file;
            }
        }
    }
}
```

### 2.7 — ENG-02 Stability Gate

```powershell
.\CodeAtlas.exe index D:\Projects\C#\repositorydb --store-local

# Then verify:
sqlite3 "D:\Projects\C#\repositorydb\.agent-index\atlas.db" `
    "SELECT symbol_type, symbol_name, file_path, start_line FROM symbols LIMIT 30;"

# Expect: real symbol names, matching file paths, sensible line numbers
# Spot-check 3-4 results against actual source files manually
```

---

## ENG-03 — SQLite Schema & Migrations (16 hours)

### 3.1 — Connection Factory (`Db/CodeAtlasDb.cs`)

```csharp
using Microsoft.Data.Sqlite;
using SimpleMigrations;
using SimpleMigrations.DatabaseProvider;
using CodeAtlas.Core.Db.Queries;

namespace CodeAtlas.Core.Db;

public sealed class CodeAtlasDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public SymbolQueries Symbols { get; }
    public ReferenceQueries References { get; }

    public CodeAtlasDb(string dbPath)
    {
        // Ensure the directory exists before SQLite tries to create the file
        var dir = Path.GetDirectoryName(dbPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
        _connection.Open();

        // WAL mode for better concurrent read performance (Phase 3 will need this)
        using var wal = _connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();

        Symbols    = new SymbolQueries(_connection);
        References = new ReferenceQueries(_connection);
    }

    public void Migrate()
    {
        var migrator = new SimpleMigrator(
            typeof(Migrations.M001_InitialSchema).Assembly,
            new SqliteDatabaseProvider(_connection));
        migrator.Load();
        migrator.MigrateToLatest();
    }

    public void Dispose() => _connection.Dispose();
}
```

### 3.2 — Migration M001 — Initial Schema

```csharp
using SimpleMigrations;

namespace CodeAtlas.Core.Db.Migrations;

[Migration(1, "Initial schema")]
public class M001_InitialSchema : Migration
{
    protected override void Up()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS index_state (
                branch            TEXT NOT NULL,
                last_commit_hash  TEXT PRIMARY KEY,
                indexed_at        TEXT NOT NULL,
                dirty_files_count INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS namespaces (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                name     TEXT    UNIQUE NOT NULL,
                language TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS symbols (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                namespace_id INTEGER REFERENCES namespaces(id) ON DELETE SET NULL,
                class_name   TEXT,
                symbol_type  TEXT NOT NULL,
                symbol_name  TEXT NOT NULL,
                language     TEXT NOT NULL DEFAULT 'csharp',
                file_path    TEXT NOT NULL,
                start_line   INTEGER NOT NULL,
                end_line     INTEGER NOT NULL,
                start_byte   INTEGER NOT NULL,
                end_byte     INTEGER NOT NULL,
                signature    TEXT,
                doc_comment  TEXT
            );

            CREATE TABLE IF NOT EXISTS symbol_references (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                caller_symbol_id INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
                target_symbol_id INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
                reference_type   TEXT    NOT NULL,
                file_path        TEXT    NOT NULL,
                line_number      INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cross_stack_mappings (
                id                     INTEGER PRIMARY KEY AUTOINCREMENT,
                dto_name               TEXT    NOT NULL,
                cs_class_id            INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
                ts_interface_id        INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
                angular_component_path TEXT
            );
            """);
    }

    protected override void Down()
    {
        Execute("""
            DROP TABLE IF EXISTS cross_stack_mappings;
            DROP TABLE IF EXISTS symbol_references;
            DROP TABLE IF EXISTS symbols;
            DROP TABLE IF EXISTS namespaces;
            DROP TABLE IF EXISTS index_state;
            """);
    }
}
```

### 3.3 — Migration M002 — Performance Indices

```csharp
using SimpleMigrations;

namespace CodeAtlas.Core.Db.Migrations;

[Migration(2, "Performance indices")]
public class M002_Indices : Migration
{
    protected override void Up()
    {
        Execute("""
            CREATE INDEX IF NOT EXISTS idx_symbols_name     ON symbols(symbol_name);
            CREATE INDEX IF NOT EXISTS idx_symbols_file     ON symbols(file_path);
            CREATE INDEX IF NOT EXISTS idx_symbols_type     ON symbols(symbol_type);
            CREATE INDEX IF NOT EXISTS idx_symbols_language ON symbols(language);
            CREATE INDEX IF NOT EXISTS idx_refs_caller      ON symbol_references(caller_symbol_id);
            CREATE INDEX IF NOT EXISTS idx_refs_target      ON symbol_references(target_symbol_id);
            """);
    }

    protected override void Down()
    {
        Execute("""
            DROP INDEX IF EXISTS idx_symbols_name;
            DROP INDEX IF EXISTS idx_symbols_file;
            DROP INDEX IF EXISTS idx_symbols_type;
            DROP INDEX IF EXISTS idx_symbols_language;
            DROP INDEX IF EXISTS idx_refs_caller;
            DROP INDEX IF EXISTS idx_refs_target;
            """);
    }
}
```

### 3.4 — Symbol Queries (`Db/Queries/SymbolQueries.cs`)

All SQL lives here. No inline SQL in orchestrators, parsers, or CLI handlers.

```csharp
using Microsoft.Data.Sqlite;
using CodeAtlas.Models;

namespace CodeAtlas.Core.Db.Queries;

public sealed class SymbolQueries(SqliteConnection connection)
{
    /// <summary>
    /// Delete all symbols for a given file path, then insert the new set.
    /// This is the Phase 1 upsert strategy — full file replacement.
    /// Phase 2 will upgrade this to true delta reconciliation.
    /// </summary>
    public void UpsertFile(string filePath, IReadOnlyList<Symbol> symbols)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            // 1. Delete existing symbols for this file
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM symbols WHERE file_path = @filePath";
            delete.Parameters.AddWithValue("@filePath", filePath);
            delete.Transaction = tx;
            delete.ExecuteNonQuery();

            // 2. Insert new symbols
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO symbols
                    (class_name, symbol_type, symbol_name, language, file_path,
                     start_line, end_line, start_byte, end_byte, signature, doc_comment)
                VALUES
                    (@className, @symbolType, @symbolName, @language, @filePath,
                     @startLine, @endLine, @startByte, @endByte, @signature, @docComment)
                """;
            insert.Transaction = tx;

            // Reuse parameters across inserts (more efficient)
            var p = new
            {
                className  = insert.Parameters.Add("@className",  SqliteType.Text),
                symbolType = insert.Parameters.Add("@symbolType", SqliteType.Text),
                symbolName = insert.Parameters.Add("@symbolName", SqliteType.Text),
                language   = insert.Parameters.Add("@language",   SqliteType.Text),
                filePath   = insert.Parameters.Add("@filePath",   SqliteType.Text),
                startLine  = insert.Parameters.Add("@startLine",  SqliteType.Integer),
                endLine    = insert.Parameters.Add("@endLine",    SqliteType.Integer),
                startByte  = insert.Parameters.Add("@startByte",  SqliteType.Integer),
                endByte    = insert.Parameters.Add("@endByte",    SqliteType.Integer),
                signature  = insert.Parameters.Add("@signature",  SqliteType.Text),
                docComment = insert.Parameters.Add("@docComment", SqliteType.Text),
            };

            foreach (var sym in symbols)
            {
                p.className.Value  = (object?)sym.ClassName  ?? DBNull.Value;
                p.symbolType.Value = sym.SymbolType.ToString();
                p.symbolName.Value = sym.SymbolName;
                p.language.Value   = sym.Language ?? "unknown";
                p.filePath.Value   = filePath;
                p.startLine.Value  = sym.StartLine;
                p.endLine.Value    = sym.EndLine;
                p.startByte.Value  = sym.StartByte;
                p.endByte.Value    = sym.EndByte;
                p.signature.Value  = (object?)sym.Signature  ?? DBNull.Value;
                p.docComment.Value = (object?)sym.DocComment ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Find symbols by name (case-insensitive LIKE match).</summary>
    public IReadOnlyList<Symbol> FindByName(string symbolName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT symbol_name, symbol_type, class_name, language,
                   file_path, start_line, end_line, start_byte, end_byte,
                   signature, doc_comment
            FROM symbols
            WHERE symbol_name LIKE @name COLLATE NOCASE
            ORDER BY file_path, start_line
            LIMIT 50
            """;
        cmd.Parameters.AddWithValue("@name", symbolName);

        var results = new List<Symbol>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Symbol
            {
                SymbolName = reader.GetString(0),
                SymbolType = Enum.Parse<SymbolType>(reader.GetString(1)),
                ClassName  = reader.IsDBNull(2) ? null : reader.GetString(2),
                Language   = reader.IsDBNull(3) ? null : reader.GetString(3),
                FilePath   = reader.GetString(4),
                StartLine  = reader.GetInt32(5),
                EndLine    = reader.GetInt32(6),
                StartByte  = reader.GetInt32(7),
                EndByte    = reader.GetInt32(8),
                Signature  = reader.IsDBNull(9)  ? null : reader.GetString(9),
                DocComment = reader.IsDBNull(10) ? null : reader.GetString(10),
            });
        }
        return results;
    }

    /// <summary>Count symbols per file — useful for sanity checks during development.</summary>
    public IReadOnlyDictionary<string, int> CountPerFile()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT file_path, COUNT(*) FROM symbols GROUP BY file_path ORDER BY file_path";
        var result = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }
}
```

### 3.5 — Reference Queries Placeholder (`Db/Queries/ReferenceQueries.cs`)

```csharp
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Core.Db.Queries;

/// <summary>
/// Placeholder — populated in Phase 3 (MCP-02: FindSymbolCallers).
/// </summary>
public sealed class ReferenceQueries(SqliteConnection connection)
{
    // Phase 3: FindCallers, FindCallees, GetDiffImpact
}
```

### 3.6 — ENG-03 Stability Gate

```powershell
# Run index twice — verify idempotency
.\CodeAtlas.exe index D:\Projects\C#\repositorydb --store-local
.\CodeAtlas.exe index D:\Projects\C#\repositorydb --store-local

# Count rows — must be identical both times
sqlite3 "D:\Projects\C#\repositorydb\.agent-index\atlas.db" "SELECT COUNT(*) FROM symbols;"

# Check no duplicates
sqlite3 "D:\Projects\C#\repositorydb\.agent-index\atlas.db" `
    "SELECT symbol_name, file_path, COUNT(*) AS c FROM symbols GROUP BY symbol_name, file_path HAVING c > 1 LIMIT 10;"
# Expected: 0 rows
```

---

## Tests (xUnit)

### Test Fixtures

**`tests/CodeAtlas.Tests/Fixtures/SampleClass.cs`**
```csharp
namespace Fixtures.Sample;

/// <summary>A sample person entity for parser testing.</summary>
public class PersonService
{
    public string Name { get; set; } = string.Empty;

    public string GetGreeting(string prefix) => $"{prefix} {Name}";

    private void PrivateHelper() { }
}

public interface IPersonRepository
{
    PersonService? GetById(int id);
}
```

**`tests/CodeAtlas.Tests/Fixtures/SampleComponent.ts`**
```typescript
export interface UserProfile {
    id: number;
    name: string;
}

export class UserService {
    getUser(id: number): UserProfile | null {
        return null;
    }
}

type UserId = number;
```

### Parser Tests (`tests/CodeAtlas.Tests/ParserTests.cs`)

```csharp
using CodeAtlas.Models;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests;

public class CSharpParserTests
{
    private static readonly CSharpParser Parser = new();

    private static string FixtureSource(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    [Fact]
    public void Parse_SampleClass_ExtractsExpectedSymbols()
    {
        var source  = FixtureSource("SampleClass.cs");
        var symbols = Parser.Parse("Fixtures/SampleClass.cs", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "PersonService" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "GetGreeting"   && s.SymbolType == SymbolType.Method);
        Assert.Contains(symbols, s => s.SymbolName == "Name"          && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "IPersonRepository" && s.SymbolType == SymbolType.Interface);
    }

    [Fact]
    public void Parse_SampleClass_LineNumbersAreCorrect()
    {
        var source  = FixtureSource("SampleClass.cs");
        var symbols = Parser.Parse("Fixtures/SampleClass.cs", source).ToList();

        var cls = symbols.Single(s => s.SymbolName == "PersonService");
        Assert.True(cls.StartLine > 0);
        Assert.True(cls.EndLine >= cls.StartLine);
    }

    [Fact]
    public void Parse_EmptySource_ReturnsNoSymbols()
    {
        var symbols = Parser.Parse("empty.cs", "").ToList();
        Assert.Empty(symbols);
    }

    [Fact]
    public void Parse_MalformedSource_DoesNotThrow()
    {
        var symbols = Parser.Parse("bad.cs", "class { !!!invalid c#").ToList();
        // Tree-sitter is error-tolerant — may or may not yield symbols, but must not throw
    }
}

public class TypeScriptParserTests
{
    private static readonly TypeScriptParser Parser = new();

    private static string FixtureSource(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    [Fact]
    public void Parse_SampleComponent_ExtractsInterfaceAndClass()
    {
        var source  = FixtureSource("SampleComponent.ts");
        var symbols = Parser.Parse("Fixtures/SampleComponent.ts", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "UserProfile" && s.SymbolType == SymbolType.Interface);
        Assert.Contains(symbols, s => s.SymbolName == "UserService"  && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "getUser"      && s.SymbolType == SymbolType.Method);
        Assert.Contains(symbols, s => s.SymbolName == "UserId"       && s.SymbolType == SymbolType.TypeAlias);
    }
}
```

---

## Known Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
| :--- | :---: | :---: | :--- |
| `TreeSitter` NuGet version doesn't match grammar package version | Medium | High | Pin all `TreeSitter.*` packages to the same version; verify with a simple `Hello World` parse before building the full parser |
| Native `e_sqlite3.dll` missing from AOT publish output | Low | High | Check publish output immediately after ENG-01; add `<NativeFileReference>` in `.csproj` if needed |
| Tree-sitter S-expression query syntax differs from grammar node types | Medium | Medium | Validate each query incrementally with a REPL tool or small harness; consult the grammar `.js` files in the NuGet package source |
| `SimpleMigrations` reflection usage incompatible with AOT | Medium | High | Test migration runner in AOT publish early (ENG-01); if it fails, switch to hand-written migration runner using raw SQL |
| C# namespace extraction is complex (nested namespaces, file-scoped) | Medium | Low | Defer namespace resolution to ENG-03 follow-up; store `null` for namespace and resolve in Phase 2 |

---

## Execution Order

```
Day 1-2: ENG-01 — Solution scaffold, packages, csproj, StorageConfig, Program.cs CLI skeleton
         → Gate: dotnet publish AOT succeeds, --help prints correctly

Day 3-5: ENG-03 — CodeAtlasDb, migrations M001+M002, SymbolQueries (write path)
         → Gate: fresh DB creates all 5 tables with correct schema

Day 6-10: ENG-02 — Models, ILanguageParser, CSharpParser, TypeScriptParser, stubs, IndexOrchestrator
          → Gate: index command produces symbols in DB, verified against source files

Day 11-12: Tests — xUnit fixture setup and parser assertions
           → Gate: all tests pass, duplicates check passes, idempotency confirmed

Day 13-14: Buffer — AOT trim warning cleanup, edge cases (empty files, binary files, symlinks)
```

---

## Deliverables at Phase 1 Completion

| Deliverable | Description |
| :--- | :--- |
| `CodeAtlas.exe` | Single-file win-x64 native executable (AOT compiled) |
| SQLite schema | 5 tables, 6 indices, versioned via `SimpleMigrations` |
| C# parser | Full symbol extraction (class, interface, struct, enum, method, property) |
| TypeScript parser | Full symbol extraction (interface, class, function, method, type alias) |
| HTML / PS1 parsers | Registered stubs (no-op, ready for Phase 1 stretch or Phase 2) |
| xUnit tests | Parser correctness tests against known fixtures |
| `index` CLI command | Full directory walk, parallel parsing, transactional batch writes |
| `query` CLI command | Symbol lookup by name with formatted output |
| Phase 2 readiness | `CodeAtlasDb`, `SymbolQueries.UpsertFile`, and `IndexOrchestrator` designed for delta upgrade |
