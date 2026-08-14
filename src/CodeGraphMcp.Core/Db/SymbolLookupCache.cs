using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Db;

/// <summary>
/// High-performance in-memory index of symbols for fast O(1) ID resolution
/// during reference insertion, eliminating N+1 SQL SELECT queries.
/// </summary>
public sealed class SymbolLookupCache
{
    private sealed record SymbolEntry(long Id, string SymbolName, string FilePath, int StartLine);

    private readonly Dictionary<string, List<SymbolEntry>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SymbolEntry>> _byFile = new(StringComparer.OrdinalIgnoreCase);

    public static SymbolLookupCache BuildFromDb(SqliteConnection connection)
    {
        var cache = new SymbolLookupCache();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, symbol_name, file_path, start_line FROM symbols ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var name = reader.GetString(1);
            var file = reader.GetString(2);
            var line = reader.GetInt32(3);
            cache.Add(id, name, file, line);
        }
        return cache;
    }

    public void Add(long id, string symbolName, string filePath, int startLine)
    {
        var entry = new SymbolEntry(id, symbolName, filePath, startLine);

        if (!_byName.TryGetValue(symbolName, out var nameList))
        {
            nameList = [];
            _byName[symbolName] = nameList;
        }
        nameList.Add(entry);

        if (!_byFile.TryGetValue(filePath, out var fileList))
        {
            fileList = [];
            _byFile[filePath] = fileList;
        }
        fileList.Add(entry);
    }

    public void MergeFrom(SymbolLookupCache other)
    {
        foreach (var namePair in other._byName)
        {
            if (!_byName.TryGetValue(namePair.Key, out var nameList))
            {
                nameList = [];
                _byName[namePair.Key] = nameList;
            }
            nameList.AddRange(namePair.Value);
        }

        foreach (var filePair in other._byFile)
        {
            if (!_byFile.TryGetValue(filePair.Key, out var fileList))
            {
                fileList = [];
                _byFile[filePair.Key] = fileList;
            }
            fileList.AddRange(filePair.Value);
        }
    }

    public long? ResolveId(string symbolName, string? preferredFilePath = null)
    {
        if (string.IsNullOrEmpty(symbolName)) return null;

        if (!_byName.TryGetValue(symbolName, out var list) || list.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(preferredFilePath))
        {
            var match = list.FirstOrDefault(e => string.Equals(e.FilePath, preferredFilePath, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }

        return list[0].Id;
    }

    public long? GetPrimarySymbolIdForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        if (!_byFile.TryGetValue(filePath, out var list) || list.Count == 0)
            return null;

        return list.OrderBy(e => e.StartLine).First().Id;
    }
}
