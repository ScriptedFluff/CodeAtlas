using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class AbstractionsTests
{
    [Fact]
    public void ParserMetadataAttribute_ValidArguments_ConstructsCorrectly()
    {
        var attr = new ParserMetadataAttribute("python", "Python Parser", "1.2.3")
        {
            Description = "A custom Python AST parser",
            Author = "Community"
        };

        Assert.Equal("python", attr.Id);
        Assert.Equal("Python Parser", attr.Name);
        Assert.Equal("1.2.3", attr.Version);
        Assert.Equal("A custom Python AST parser", attr.Description);
        Assert.Equal("Community", attr.Author);

        var meta = attr.ToMetadata();
        Assert.Equal("python", meta.Id);
        Assert.Equal("Python Parser", meta.Name);
        Assert.Equal("1.2.3", meta.Version);
        Assert.Equal("A custom Python AST parser", meta.Description);
        Assert.Equal("Community", meta.Author);
    }

    [Theory]
    [InlineData("", "Name", "1.0.0")]
    [InlineData("id", "", "1.0.0")]
    [InlineData("id", "Name", "")]
    [InlineData("  ", "Name", "1.0.0")]
    public void ParserMetadataAttribute_InvalidArguments_ThrowsArgumentException(string id, string name, string version)
    {
        Assert.Throws<ArgumentException>(() => new ParserMetadataAttribute(id, name, version));
    }

    [ParserMetadata("custom-lua", "Custom Lua Parser", "2.0.0", Description = "Custom Lua AST parser", Author = "LuaDev")]
    private sealed class AttributedTestParser : ILanguageParser
    {
        public IReadOnlyList<string> FileExtensions => [".lua"];
        public string Language => "lua";
        public IEnumerable<Symbol> Parse(string filePath, string source) => [];
    }

    private sealed class UnattributedTestParser : ILanguageParser
    {
        public IReadOnlyList<string> FileExtensions => [".xyz"];
        public string Language => "xyz";
        public IEnumerable<Symbol> Parse(string filePath, string source) => [];
    }

    [ParserMetadata("custom-ruby", "Custom Ruby Parser", "3.0.0", Description = "Ruby AST parser", Author = "Rubyist")]
    private sealed class BaseDerivedTestParser : LanguageParserBase
    {
        public override IReadOnlyList<string> FileExtensions => [".rb"];
        public override string Language => "ruby";
        public override IEnumerable<Symbol> Parse(string filePath, string source) => [];
    }

    [Fact]
    public void ILanguageParser_WithAttribute_ResolvesAttributeMetadata()
    {
        ILanguageParser parser = new AttributedTestParser();

        Assert.NotNull(parser.Metadata);
        Assert.Equal("custom-lua", parser.Metadata.Id);
        Assert.Equal("Custom Lua Parser", parser.Metadata.Name);
        Assert.Equal("2.0.0", parser.Metadata.Version);
        Assert.Equal("Custom Lua AST parser", parser.Metadata.Description);
        Assert.Equal("LuaDev", parser.Metadata.Author);
    }

    [Fact]
    public void ILanguageParser_WithoutAttribute_ResolvesFallbackMetadata()
    {
        ILanguageParser parser = new UnattributedTestParser();

        Assert.NotNull(parser.Metadata);
        Assert.Equal("xyz", parser.Metadata.Id);
        Assert.Equal("xyz Parser", parser.Metadata.Name);
        Assert.Equal("1.0.0", parser.Metadata.Version);
        Assert.Null(parser.Metadata.Description);
        Assert.Null(parser.Metadata.Author);
    }

    [Fact]
    public void LanguageParserBase_WithAttribute_ResolvesDirectly()
    {
        var parser = new BaseDerivedTestParser();

        Assert.NotNull(parser.Metadata);
        Assert.Equal("custom-ruby", parser.Metadata.Id);
        Assert.Equal("Custom Ruby Parser", parser.Metadata.Name);
        Assert.Equal("3.0.0", parser.Metadata.Version);
        Assert.Equal("Ruby AST parser", parser.Metadata.Description);
        Assert.Equal("Rubyist", parser.Metadata.Author);

        var fileResult = parser.ParseFile("main.rb", "puts 'hello'");
        Assert.NotNull(fileResult);
        Assert.Empty(fileResult.Symbols);
        Assert.Empty(fileResult.References);
    }

    [Fact]
    public void ILanguageParser_ParseFile_CombinesSymbolsAndReferences()
    {
        ILanguageParser parser = new AttributedTestParser();
        var result = parser.ParseFile("test.lua", "print('hello')");

        Assert.NotNull(result);
        Assert.Empty(result.Symbols);
        Assert.Empty(result.References);
    }

    [Fact]
    public void Symbol_And_SymbolReference_RecordProperties()
    {
        var symbol = new Symbol
        {
            Id = 1,
            SymbolName = "MyClass",
            SymbolType = SymbolType.Class,
            ClassName = null,
            Namespace = "MyNamespace",
            Language = "csharp",
            FilePath = "src/MyClass.cs",
            StartLine = 1,
            EndLine = 10,
            StartByte = 0,
            EndByte = 200,
            Signature = "public class MyClass",
            DocComment = "Sample doc",
            IsPartial = false
        };

        Assert.Equal(1, symbol.Id);
        Assert.Equal("MyClass", symbol.SymbolName);
        Assert.Equal(SymbolType.Class, symbol.SymbolType);
        Assert.Equal("csharp", symbol.Language);

        var reference = new SymbolReference(
            CallerSymbolName: "MyMethod",
            TargetSymbolName: "TargetMethod",
            ReferenceType: "Call",
            FilePath: "src/MyClass.cs",
            LineNumber: 5);

        Assert.Equal("MyMethod", reference.CallerSymbolName);
        Assert.Equal("TargetMethod", reference.TargetSymbolName);
        Assert.Equal("Call", reference.ReferenceType);
        Assert.Equal(5, reference.LineNumber);
    }
}
