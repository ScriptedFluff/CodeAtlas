using CodeAtlas.Models;

namespace CodeAtlas.Core.Filtering;

/// <summary>
/// Container for keyset cursor paginated query results.
/// </summary>
public sealed class FilterQueryResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required long NextCursor { get; init; }
    public required bool HasMore { get; init; }
    public required int TotalReturned { get; init; }
    public required string? Filter { get; init; }
    public required bool TranspiledToSql { get; init; }
}
