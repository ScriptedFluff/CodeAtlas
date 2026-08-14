using CodeGraphMcp.Config;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class PowerShellParserTests
{
    private readonly PowerShellParser _parser = new();

    [Fact]
    public void Parse_ExtractsFunctionsAndParameters()
    {
        var psScript = """
            # Synopsis: Test function for code graph
            function Get-ProjectStatus {
                param(
                    [string]$ProjectPath,
                    [int]$Timeout
                )
                Get-ChildItem -Path $ProjectPath
            }

            filter Filter-Output {
                param([string]$InputText)
                Write-Output $InputText
            }
            """;

        var symbols = _parser.Parse("scripts/status.ps1", psScript).ToList();

        Assert.NotEmpty(symbols);
        
        var getStatusFunc = symbols.FirstOrDefault(s => s.SymbolName == "Get-ProjectStatus");
        Assert.NotNull(getStatusFunc);
        Assert.Equal(SymbolType.Function, getStatusFunc.SymbolType);
        Assert.Equal("powershell", getStatusFunc.Language);
        Assert.Contains("Synopsis", getStatusFunc.DocComment ?? "");

        var filterFunc = symbols.FirstOrDefault(s => s.SymbolName == "Filter-Output");
        Assert.NotNull(filterFunc);
        Assert.Equal(SymbolType.Function, filterFunc.SymbolType);

        var paramsExtracted = symbols.Where(s => s.SymbolType == SymbolType.Variable).ToList();
        Assert.Contains(paramsExtracted, p => p.SymbolName == "$ProjectPath");
        Assert.Contains(paramsExtracted, p => p.SymbolName == "$Timeout");
    }

    [Fact]
    public void Parse_ExtractsScriptLevelSymbolAndTopLevelParams()
    {
        var psScript = """
            [CmdletBinding()]
            param(
                [string]$Get-TemplateParameters,
                [string]$ResourceGroupName
            )

            Write-Output "Configuring security..."
            """;

        var symbols = _parser.Parse("src/Clever.Services.ResourceGroup/Scripts/Configure-Security.ps1", psScript).ToList();

        Assert.NotEmpty(symbols);

        var scriptSymbol = symbols.FirstOrDefault(s => s.SymbolName == "Configure-Security");
        Assert.NotNull(scriptSymbol);
        Assert.Equal(SymbolType.Function, scriptSymbol.SymbolType);
        Assert.Equal("powershell", scriptSymbol.Language);

        var templateParam = symbols.FirstOrDefault(s => s.SymbolName == "$Get-TemplateParameters");
        Assert.NotNull(templateParam);
        Assert.Equal(SymbolType.Variable, templateParam.SymbolType);
    }

    [Fact]
    public void LanguageNormalizer_NormalizesAliasesCorrectly()
    {
        Assert.Equal("powershell", LanguageNormalizer.Normalize("ps1"));
        Assert.Equal("powershell", LanguageNormalizer.Normalize("ps"));
        Assert.Equal("powershell", LanguageNormalizer.Normalize("powershell"));
        Assert.Equal("powershell", LanguageNormalizer.Normalize(".ps1"));

        Assert.Equal("csharp", LanguageNormalizer.Normalize("cs"));
        Assert.Equal("csharp", LanguageNormalizer.Normalize("c#"));
        Assert.Equal("csharp", LanguageNormalizer.Normalize("csharp"));

        Assert.Equal("typescript", LanguageNormalizer.Normalize("ts"));
        Assert.Equal("javascript", LanguageNormalizer.Normalize("js"));
        Assert.Equal("python", LanguageNormalizer.Normalize("py"));
        Assert.Equal("yaml", LanguageNormalizer.Normalize("yml"));
        Assert.Null(LanguageNormalizer.Normalize(null));
    }

    [Fact]
    public void ParseReferences_ExtractsCmdletInvocations()
    {
        var psScript = """
            function Invoke-Deploy {
                Get-ChildItem -Path "C:\Source"
                Write-Output "Deployed"
            }
            """;

        var references = _parser.ParseReferences("scripts/deploy.ps1", psScript).ToList();

        Assert.NotEmpty(references);
        Assert.Contains(references, r => r.TargetSymbolName == "Get-ChildItem");
        Assert.Contains(references, r => r.TargetSymbolName == "Write-Output");
    }
}
