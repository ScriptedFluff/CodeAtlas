using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace CodeAtlas.Core.Filtering;

/// <summary>
/// Result of transpiling a pattern or regex filter into a SQLite WHERE clause.
/// </summary>
public sealed record FilterTranspileResult(
    bool IsTranslatable,
    string? SqlClause,
    IReadOnlyList<SqliteParameter> Parameters,
    Regex? FallbackRegex,
    Regex RegexMatcher,
    string OriginalPattern);

