using CodeGraphMcp.Filtering;
using Xunit;

namespace CodeGraphMcp.Tests;

public class QueryPatternClassifierTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("UserService")]
    [InlineData("my_variable")]
    [InlineData("MyNamespace:MyClass")]
    [InlineData("app-config")]
    [InlineData("Customer123")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_PureIdentifiers_ReturnsPureIdentifier(string? input)
    {
        var kind = QueryPatternClassifier.Classify(input);
        Assert.Equal(QueryPatternKind.PureIdentifier, kind);
    }

    [Theory]
    [InlineData("*Dto")]
    [InlineData("Get*")]
    [InlineData("Get*Request")]
    [InlineData("I[A-Z]*")]
    [InlineData("(Request|Response)")]
    [InlineData("?ser")]
    [InlineData("^Start")]
    [InlineData("End$")]
    public void Classify_GlobPatterns_ReturnsGlobPattern(string input)
    {
        var kind = QueryPatternClassifier.Classify(input);
        Assert.Equal(QueryPatternKind.GlobPattern, kind);
    }

    [Theory]
    [InlineData("(?=.*Service)")]
    [InlineData("(?!.*Internal)")]
    [InlineData(@"(?<=Prefix)Body")]
    [InlineData(@"(?<!Prefix)Body")]
    [InlineData(@"\bWord\b")]
    [InlineData(@"\d+")]
    [InlineData(@"\w+")]
    [InlineData(@"Item{1,3}")]
    [InlineData(@"Item+")]
    [InlineData(@"Item*?")]
    public void Classify_ComplexRegex_ReturnsComplexRegex(string input)
    {
        var kind = QueryPatternClassifier.Classify(input);
        Assert.Equal(QueryPatternKind.ComplexRegex, kind);
    }
}
