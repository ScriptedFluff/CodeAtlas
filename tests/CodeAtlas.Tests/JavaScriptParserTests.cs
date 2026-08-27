using CodeAtlas.Models;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests;

public class JavaScriptParserTests
{
    private static readonly JavaScriptParser Parser = new();

    [Fact]
    public void Parse_StandardJavaScript_ExtractsClassesFunctionsAndArrowFunctions()
    {
        var source = """
            class Calculator {
                add(a, b) {
                    return a + b;
                }
            }

            function multiply(x, y) {
                return x * y;
            }

            const divide = (a, b) => {
                return a / b;
            };
            """;

        var symbols = Parser.Parse("math.js", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "Calculator" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "add" && s.SymbolType == SymbolType.Method);
        Assert.Contains(symbols, s => s.SymbolName == "multiply" && s.SymbolType == SymbolType.Function);
        Assert.Contains(symbols, s => s.SymbolName == "divide" && s.SymbolType == SymbolType.Function);
    }

    [Fact]
    public void Parse_ReactJsxComponent_ExtractsFunctionalComponent()
    {
        var source = """
            import React from 'react';

            export function Header(props) {
                return <h1>{props.title}</h1>;
            }

            const Button = ({ onClick, children }) => {
                return <button onClick={onClick}>{children}</button>;
            };
            """;

        var symbols = Parser.Parse("Header.jsx", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "Header" && s.SymbolType == SymbolType.Function);
        Assert.Contains(symbols, s => s.SymbolName == "Button" && s.SymbolType == SymbolType.Function);
    }

    [Fact]
    public void ParseReferences_ExtractsCallsNewAndImports()
    {
        var source = """
            import { formatDate } from './utils';
            const path = require('path');

            class ReportGenerator {
                generate() {
                    const date = formatDate(new Date());
                    console.log(date);
                }
            }
            """;

        var refs = Parser.ParseReferences("ReportGenerator.js", source).ToList();

        Assert.Contains(refs, r => r.TargetSymbolName == "formatDate" && r.ReferenceType == "Call");
        Assert.Contains(refs, r => r.TargetSymbolName == "Date" && r.ReferenceType == "TypeUsage");
        Assert.Contains(refs, r => r.TargetSymbolName == "./utils" && r.ReferenceType == "Import");
        Assert.Contains(refs, r => r.TargetSymbolName == "path" && r.ReferenceType == "Import");
    }

    [Fact]
    public void Parse_MalformedJavaScript_DoesNotThrow()
    {
        var symbols = Parser.Parse("bad.js", "const = function { invalid js syntax $$$").ToList();
        var refs = Parser.ParseReferences("bad.js", "const = function { invalid js syntax $$$").ToList();

        Assert.NotNull(symbols);
        Assert.NotNull(refs);
    }
}
