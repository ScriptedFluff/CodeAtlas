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
