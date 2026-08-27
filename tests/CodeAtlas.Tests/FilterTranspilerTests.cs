using CodeAtlas.Core.Filtering;
using Xunit;

namespace CodeAtlas.Tests;

public class FilterTranspilerTests
{
    [Fact]
    public void Transpile_NullOrEmpty_ReturnsTranslatableNullClause()
    {
        var res1 = FilterTranspiler.Transpile(null);
        Assert.True(res1.IsTranslatable);
        Assert.Null(res1.SqlClause);
        Assert.Empty(res1.Parameters);
        Assert.Matches(res1.RegexMatcher, "AnySymbol");

        var res2 = FilterTranspiler.Transpile("   ");
        Assert.True(res2.IsTranslatable);
        Assert.Null(res2.SqlClause);
    }

    [Fact]
    public void Transpile_Alternations_GeneratesSqlOr()
    {
        var res = FilterTranspiler.Transpile("(Request|Response|Event)");
        Assert.True(res.IsTranslatable);
        Assert.NotNull(res.SqlClause);
        Assert.Contains(" OR ", res.SqlClause);
        Assert.Equal(3, res.Parameters.Count);
        Assert.Equal("%Request%", res.Parameters[0].Value);
        Assert.Equal("%Response%", res.Parameters[1].Value);
        Assert.Equal("%Event%", res.Parameters[2].Value);
        Assert.Matches(res.RegexMatcher, "GetOrderRequest");
        Assert.Matches(res.RegexMatcher, "OrderResponse");
        Assert.DoesNotMatch(res.RegexMatcher, "OrderService");
    }

    [Fact]
    public void Transpile_SingleCharDot_GeneratesSqlUnderscore()
    {
        var res = FilterTranspiler.Transpile("((.equest)|(Response))");
        Assert.True(res.IsTranslatable);
        Assert.NotNull(res.SqlClause);
        Assert.Contains(" OR ", res.SqlClause);
        Assert.Equal(2, res.Parameters.Count);
        Assert.Equal("%_equest%", res.Parameters[0].Value);
        Assert.Equal("%Response%", res.Parameters[1].Value);
        Assert.Matches(res.RegexMatcher, "GetRequest");
        Assert.Matches(res.RegexMatcher, "GetBequest");
        Assert.Matches(res.RegexMatcher, "MyResponse");
        Assert.DoesNotMatch(res.RegexMatcher, "OrderQuery");
    }

    [Fact]
    public void Transpile_GlobRanges_GeneratesSqlGlob()
    {
        var res = FilterTranspiler.Transpile("^I[A-Z].*");
        Assert.True(res.IsTranslatable);
        Assert.NotNull(res.SqlClause);
        Assert.Contains("GLOB", res.SqlClause);
        Assert.Single(res.Parameters);
        Assert.Equal("I[A-Z]*", res.Parameters[0].Value);
        Assert.Matches(res.RegexMatcher, "IUserService");
        Assert.DoesNotMatch(res.RegexMatcher, "Item");
    }

    [Fact]
    public void Transpile_PrefixAndSuffixAnchors_GeneratesCorrectSql()
    {
        var resPrefix = FilterTranspiler.Transpile("^Get");
        Assert.True(resPrefix.IsTranslatable);
        Assert.Single(resPrefix.Parameters);
        Assert.Equal("Get%", resPrefix.Parameters[0].Value);

        var resSuffix = FilterTranspiler.Transpile("Controller$");
        Assert.True(resSuffix.IsTranslatable);
        Assert.Single(resSuffix.Parameters);
        Assert.Equal("%Controller", resSuffix.Parameters[0].Value);

        var resExact = FilterTranspiler.Transpile("^OrderDto$");
        Assert.True(resExact.IsTranslatable);
        Assert.Single(resExact.Parameters);
        Assert.Equal("OrderDto", resExact.Parameters[0].Value);
    }

    [Fact]
    public void Transpile_ComplexLookahead_TriggersRegexFallback()
    {
        var res = FilterTranspiler.Transpile("(?=.*Service)(?!.*Internal)");
        Assert.False(res.IsTranslatable);
        Assert.Null(res.SqlClause);
        Assert.NotNull(res.FallbackRegex);
        Assert.Matches(res.FallbackRegex, "OrderService");
        Assert.DoesNotMatch(res.FallbackRegex, "OrderServiceInternal");
    }

    [Fact]
    public void Transpile_EscapedSpecialCharacters_PreservesEscaping()
    {
        var res = FilterTranspiler.Transpile(@"Order\.Dto");
        Assert.True(res.IsTranslatable);
        Assert.Single(res.Parameters);
        Assert.Equal("%Order.Dto%", res.Parameters[0].Value);
    }
}
