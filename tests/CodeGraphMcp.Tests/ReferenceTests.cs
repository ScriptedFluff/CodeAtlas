using CodeGraphMcp.Db;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class CSharpReferenceTests
{
    private static readonly CSharpParser Parser = new();

    [Fact]
    public void ParseReferences_CSharpClass_ExtractsMethodCallAndObjectCreation()
    {
        const string code = """
            namespace Demo;

            public class ServiceCaller : IService
            {
                public void DoWork()
                {
                    var repo = new PersonRepository();
                    repo.GetGreeting();
                }
            }
            """;

        var refs = Parser.ParseReferences("ServiceCaller.cs", code).ToList();

        Assert.NotEmpty(refs);
        Assert.Contains(refs, r => r.TargetSymbolName == "PersonRepository" && r.ReferenceType == "TypeUsage");
        Assert.Contains(refs, r => r.TargetSymbolName == "GetGreeting" && r.ReferenceType == "Call" && r.CallerSymbolName == "DoWork");
        Assert.Contains(refs, r => r.TargetSymbolName == "IService" && r.ReferenceType == "Inheritance");
    }

    [Fact]
    public void ParseReferences_EmptyFile_ReturnsEmpty()
    {
        var refs = Parser.ParseReferences("Empty.cs", "");
        Assert.Empty(refs);
    }
}

public class TypeScriptReferenceTests
{
    private static readonly TypeScriptParser Parser = new();

    [Fact]
    public void ParseReferences_TypeScriptFile_ExtractsCallsAndNew()
    {
        const string code = """
            import { UserService } from './user.service';

            export class Controller {
                public handle() {
                    const service = new UserService();
                    service.getUser();
                }
            }
            """;

        var refs = Parser.ParseReferences("controller.ts", code).ToList();

        Assert.NotEmpty(refs);
        Assert.Contains(refs, r => r.TargetSymbolName == "UserService" && r.ReferenceType == "TypeUsage");
        Assert.Contains(refs, r => r.TargetSymbolName == "getUser" && r.ReferenceType == "Call" && r.CallerSymbolName == "handle");
        Assert.Contains(refs, r => r.TargetSymbolName == "UserService" && r.ReferenceType == "Import");
    }
}

public class ReferenceDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CodeGraphDb _db;

    public ReferenceDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codegraph_ref_test_{Guid.NewGuid():N}.db");
        _db = new CodeGraphDb(_dbPath);
        _db.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void UpsertFileReferences_FindCallers_ReturnsCorrectEntry()
    {
        // 1. Insert symbols
        var symbolsA = new[]
        {
            new Symbol { SymbolName = "CallerMethod", SymbolType = SymbolType.Method, FilePath = "FileA.cs", StartLine = 5, EndLine = 10, StartByte = 0, EndByte = 100 }
        };
        var symbolsB = new[]
        {
            new Symbol { SymbolName = "TargetMethod", SymbolType = SymbolType.Method, FilePath = "FileB.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 50 }
        };

        _db.Symbols.UpsertFile("FileA.cs", symbolsA);
        _db.Symbols.UpsertFile("FileB.cs", symbolsB);

        // 2. Insert reference from CallerMethod -> TargetMethod
        var refs = new[]
        {
            new SymbolReference("CallerMethod", "TargetMethod", "Call", "FileA.cs", LineNumber: 7)
        };

        _db.References.UpsertFileReferences(
            "FileA.cs",
            refs,
            (name, file) => _db.Symbols.ResolveId(name, file),
            file => _db.Symbols.GetPrimarySymbolIdForFile(file));

        // 3. Find callers of TargetMethod
        var callers = _db.References.FindCallers("TargetMethod");

        Assert.Single(callers);
        Assert.Equal("CallerMethod", callers[0].CallerName);
        Assert.Equal("FileA.cs", callers[0].FilePath);
        Assert.Equal(7, callers[0].LineNumber);
        Assert.Equal("Call", callers[0].ReferenceType);
    }

    [Fact]
    public void UpsertFileReferences_PrefersLocalFileSymbol_WhenSameNameExistsInMultipleFiles()
    {
        // Two symbols named 'CommonMethod' in FileA.cs and FileB.cs
        _db.Symbols.UpsertFile("FileA.cs", new[] { new Symbol { SymbolName = "CommonMethod", SymbolType = SymbolType.Method, FilePath = "FileA.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 10 } });
        _db.Symbols.UpsertFile("FileB.cs", new[] { new Symbol { SymbolName = "CommonMethod", SymbolType = SymbolType.Method, FilePath = "FileB.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 10 } });
        _db.Symbols.UpsertFile("FileC.cs", new[] { new Symbol { SymbolName = "Target", SymbolType = SymbolType.Method, FilePath = "FileC.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 10 } });

        var refs = new[] { new SymbolReference("CommonMethod", "Target", "Call", "FileB.cs", 3) };

        _db.References.UpsertFileReferences(
            "FileB.cs",
            refs,
            (name, file) => _db.Symbols.ResolveId(name, file),
            file => _db.Symbols.GetPrimarySymbolIdForFile(file));

        var callers = _db.References.FindCallers("Target");
        Assert.Single(callers);
        Assert.Equal("FileB.cs", callers[0].FilePath);
    }

    [Fact]
    public void UpsertFileReferences_TopLevelScript_FallsBackToPrimarySymbolForFile()
    {
        _db.Symbols.UpsertFile("Program.cs", new[] { new Symbol { SymbolName = "ProgramClass", SymbolType = SymbolType.Class, FilePath = "Program.cs", StartLine = 1, EndLine = 20, StartByte = 0, EndByte = 100 } });
        _db.Symbols.UpsertFile("Service.cs", new[] { new Symbol { SymbolName = "DoWork", SymbolType = SymbolType.Method, FilePath = "Service.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 50 } });

        // Reference where CallerSymbolName is "Program" (file name fallback), which doesn't match "ProgramClass" directly by name
        var refs = new[] { new SymbolReference("Program", "DoWork", "Call", "Program.cs", 10) };

        _db.References.UpsertFileReferences(
            "Program.cs",
            refs,
            (name, file) => _db.Symbols.ResolveId(name, file),
            file => _db.Symbols.GetPrimarySymbolIdForFile(file));

        var callers = _db.References.FindCallers("DoWork");
        Assert.Single(callers);
        Assert.Equal("ProgramClass", callers[0].CallerName);
    }
}
