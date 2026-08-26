using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Tests;

public sealed class IndexedParserTrackingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public IndexedParserTrackingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cg_parser_tracking_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private sealed class FakeLanguageParser(string id, string language, string[] extensions, string version = "1.0.0") : ILanguageParser
    {
        public string Language => language;
        public IReadOnlyList<string> FileExtensions => extensions;
        public ParserMetadata Metadata { get; } = new(id, "Fake " + language, version, "Test description", "Test Author");

        public bool CanParse(string filePath) =>
            extensions.Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<Symbol> Parse(string filePath, string sourceCode)
        {
            return
            [
                new Symbol
                {
                    SymbolName = Path.GetFileNameWithoutExtension(filePath) + "_Symbol",
                    SymbolType = SymbolType.Class,
                    Language = language,
                    FilePath = filePath,
                    StartLine = 1,
                    EndLine = 1,
                    StartByte = 0,
                    EndByte = 10
                }
            ];
        }
    }

    [Fact]
    public void IndexedParserQueries_CrudOperations_WorkCorrectly()
    {
        using var db = new CodeGraphDb(_dbPath);
        db.Migrate();

        var initial = db.IndexedParsers.GetIndexedParsers();
        Assert.Empty(initial);

        var parser1 = new FakeLanguageParser("fake-csharp", "csharp", [".cs"], "1.0.0");
        db.IndexedParsers.RecordIndexedParser(parser1.Metadata.Id, parser1.Language, parser1.FileExtensions, parser1.Metadata.Version);

        var list = db.IndexedParsers.GetIndexedParsers();
        Assert.Single(list);
        Assert.Equal("fake-csharp", list[0].ParserId);
        Assert.Equal("csharp", list[0].Language);
        Assert.Equal(".cs", list[0].Extensions);
        Assert.Equal("1.0.0", list[0].Version);

        db.IndexedParsers.RemoveIndexedParser("fake-csharp");
        var afterRemove = db.IndexedParsers.GetIndexedParsers();
        Assert.Empty(afterRemove);
    }

    [Fact]
    public async Task IndexOrchestrator_RecordsActiveParsers_OnFullIndex()
    {
        var repoDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(repoDir, "Test.cs"), "public class Test {}");

        var parser1 = new FakeLanguageParser("csharp", "csharp", [".cs"]);
        var parser2 = new FakeLanguageParser("json", "json", [".json"]);
        var registry = new ParserRegistry([parser1, parser2]);

        using var db = new CodeGraphDb(_dbPath);
        db.Migrate();

        var orchestrator = new IndexOrchestrator(db, registry);
        var result = await orchestrator.IndexAsync(repoDir);

        Assert.Equal(1, result.FilesProcessed);

        var indexedParsers = db.IndexedParsers.GetIndexedParsers();
        Assert.Equal(2, indexedParsers.Count);
        Assert.Contains(indexedParsers, p => p.ParserId == "csharp");
        Assert.Contains(indexedParsers, p => p.ParserId == "json");
    }

    [Fact]
    public async Task DeltaEngine_DetectsNewParserWithoutGitChange_AndIndexesNewFiles()
    {
        var repoDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoDir);

        // Initialize fake git repo
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "init") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.name test") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.email test@test.com") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();

        File.WriteAllText(Path.Combine(repoDir, "Service.cs"), "public class Service {}");
        File.WriteAllText(Path.Combine(repoDir, "config.json"), "{\"app\":\"demo\"}");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "add .") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "commit -m initial") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();

        // 1. First index with ONLY C# parser
        var csharpParser = new FakeLanguageParser("csharp", "csharp", [".cs"]);
        var registry1 = new ParserRegistry([csharpParser]);

        using var db = new CodeGraphDb(_dbPath);
        db.Migrate();

        var delta1 = new DeltaEngine(repoDir, db, registry1);
        var res1 = await delta1.RunAsync();
        Assert.Equal("full", res1.Mode);
        Assert.Equal(1, res1.FilesReindexed);

        var syms1 = db.Symbols.FindByName("%");
        Assert.Single(syms1);
        Assert.Equal("Service_Symbol", syms1[0].SymbolName);

        // 2. Second run with SAME registry -> should be NOOP
        var res2 = await delta1.RunAsync();
        Assert.Equal("noop", res2.Mode);

        // 3. Now add JSON parser to the registry WITHOUT any git commit or file change
        var jsonParser = new FakeLanguageParser("json", "json", [".json"]);
        var registry2 = new ParserRegistry([csharpParser, jsonParser]);

        var delta2 = new DeltaEngine(repoDir, db, registry2);
        var res3 = await delta2.RunAsync();

        // Should detect new parser and index the config.json file!
        Assert.Equal("parser-delta", res3.Mode);
        Assert.Equal(1, res3.FilesReindexed);

        var syms2 = db.Symbols.FindByName("%");
        Assert.Equal(2, syms2.Count);
        Assert.Contains(syms2, s => s.SymbolName == "Service_Symbol");
        Assert.Contains(syms2, s => s.SymbolName == "config_Symbol");

        // 4. Third run with registry2 -> should be NOOP because both are now recorded
        var res4 = await delta2.RunAsync();
        Assert.Equal("noop", res4.Mode);
    }

    [Fact]
    public async Task DeltaEngine_DetectsRemovedParser_CleansUpSymbolsAndRecord()
    {
        var repoDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoDir);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "init") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.name test") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.email test@test.com") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();

        File.WriteAllText(Path.Combine(repoDir, "Service.cs"), "public class Service {}");
        File.WriteAllText(Path.Combine(repoDir, "config.json"), "{\"app\":\"demo\"}");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "add .") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "commit -m initial") { WorkingDirectory = repoDir, UseShellExecute = false })?.WaitForExit();

        var csharpParser = new FakeLanguageParser("csharp", "csharp", [".cs"]);
        var jsonParser = new FakeLanguageParser("json", "json", [".json"]);
        var registry = new ParserRegistry([csharpParser, jsonParser]);

        using var db = new CodeGraphDb(_dbPath);
        db.Migrate();

        var delta1 = new DeltaEngine(repoDir, db, registry);
        var res1 = await delta1.RunAsync();
        Assert.Equal("full", res1.Mode);

        var syms1 = db.Symbols.FindByName("%");
        Assert.Equal(2, syms1.Count);

        // Now remove JSON parser (e.g. user disabled it)
        var registryWithoutJson = new ParserRegistry([csharpParser]);
        var delta2 = new DeltaEngine(repoDir, db, registryWithoutJson);
        var res2 = await delta2.RunAsync();

        // JSON symbols should have been purged
        var syms2 = db.Symbols.FindByName("%");
        Assert.Single(syms2);
        Assert.Equal("Service_Symbol", syms2[0].SymbolName);

        var indexed = db.IndexedParsers.GetIndexedParsers();
        Assert.Single(indexed);
        Assert.Equal("csharp", indexed[0].ParserId);
    }
}
