using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using CodeGraphMcp.Db;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Indexer;

public sealed record FileUpsertResult(
    string FilePath,
    int SymbolsUpdated,
    int SymbolsAdded,
    int SymbolsDeleted,
    int ReferencesUpdated,
    long ElapsedMs,
    string? Message = null);

public sealed record FileDeleteResult(
    string FilePath,
    int SymbolsDeleted,
    int ReferencesDeleted,
    long ElapsedMs,
    string? Message = null);

public interface ISingleFileIndexer
{
    Task<FileUpsertResult> UpsertFileAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string filePath,
        string? fileContent = null,
        CancellationToken ct = default);

    Task<FileDeleteResult> DeleteFileAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string filePath,
        CancellationToken ct = default);
}

public sealed class SingleFileIndexer : ISingleFileIndexer
{
    private readonly IParserRegistry _parserRegistry;
    private readonly ILogger<SingleFileIndexer>? _logger;

    public SingleFileIndexer(
        IParserRegistry? parserRegistry = null,
        ILogger<SingleFileIndexer>? logger = null)
    {
        _parserRegistry = parserRegistry ?? ParserRegistry.CreateDefault();
        _logger = logger;
    }

    public async Task<FileUpsertResult> UpsertFileAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string filePath,
        string? fileContent = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var relPath = WorkspacePathNormalizer.NormalizeRelativePath(workspaceRoot, filePath);

        var ext = Path.GetExtension(relPath);
        var parser = _parserRegistry.GetParserForExtension(ext);
        if (parser == null)
        {
            sw.Stop();
            _logger?.LogWarning("No parser registered for extension '{Extension}' ({FilePath})", ext, relPath);
            return new FileUpsertResult(
                relPath, 0, 0, 0, 0, sw.ElapsedMilliseconds,
                $"No parser registered for extension '{ext}'.");
        }

        string source;
        if (fileContent != null)
        {
            source = fileContent;
        }
        else
        {
            var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(workspaceRoot, relPath);
            if (!File.Exists(fullPath))
            {
                var del = await DeleteFileAsync(workspaceRoot, db, filePath, ct);
                sw.Stop();
                return new FileUpsertResult(
                    relPath, 0, 0, del.SymbolsDeleted, del.ReferencesDeleted, sw.ElapsedMilliseconds,
                    "File does not exist on disk; purged from index.");
            }

            source = await File.ReadAllTextAsync(fullPath, ct);
        }

        var parseResult = parser.ParseFile(relPath, source);

        int symbolsUpdated = 0;
        int symbolsAdded = 0;
        int symbolsDeleted = 0;
        int refsUpdated = 0;

        lock (db)
        {
            using var tx = db.Connection.BeginTransaction();

            // 1. Retrieve current symbols in database for this file
            var existingSymbols = new List<Symbol>();
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT id, symbol_name, symbol_type, class_name, language,
                           file_path, start_line, end_line, start_byte, end_byte,
                           signature, doc_comment, is_partial
                    FROM symbols
                    WHERE file_path = @filePath
                    ORDER BY start_line;
                    """;
                cmd.Parameters.AddWithValue("@filePath", relPath);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existingSymbols.Add(new Symbol
                    {
                        Id         = reader.GetInt64(0),
                        SymbolName = reader.GetString(1),
                        SymbolType = Enum.Parse<SymbolType>(reader.GetString(2)),
                        ClassName  = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Language   = reader.IsDBNull(4) ? null : reader.GetString(4),
                        FilePath   = reader.GetString(5),
                        StartLine  = reader.GetInt32(6),
                        EndLine    = reader.GetInt32(7),
                        StartByte  = reader.GetInt32(8),
                        EndByte    = reader.GetInt32(9),
                        Signature  = reader.IsDBNull(10) ? null : reader.GetString(10),
                        DocComment = reader.IsDBNull(11) ? null : reader.GetString(11),
                        IsPartial  = reader.GetInt32(12) == 1,
                    });
                }
            }

            var existingQueueMap = new Dictionary<(string Name, SymbolType Type, string? Class), Queue<Symbol>>();
            foreach (var s in existingSymbols)
            {
                var key = (s.SymbolName, s.SymbolType, s.ClassName);
                if (!existingQueueMap.TryGetValue(key, out var q))
                {
                    q = new Queue<Symbol>();
                    existingQueueMap[key] = q;
                }
                q.Enqueue(s);
            }

            var matchedOldIds = new HashSet<long>();

            using var updateCmd = db.Connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE symbols SET
                    start_line  = @startLine,
                    end_line    = @endLine,
                    start_byte  = @startByte,
                    end_byte    = @endByte,
                    signature   = @signature,
                    doc_comment = @docComment,
                    is_partial  = @isPartial
                WHERE id = @id;
                """;
            var uId         = updateCmd.Parameters.Add("@id",         SqliteType.Integer);
            var uStartLine  = updateCmd.Parameters.Add("@startLine",  SqliteType.Integer);
            var uEndLine    = updateCmd.Parameters.Add("@endLine",    SqliteType.Integer);
            var uStartByte  = updateCmd.Parameters.Add("@startByte",  SqliteType.Integer);
            var uEndByte    = updateCmd.Parameters.Add("@endByte",    SqliteType.Integer);
            var uSignature  = updateCmd.Parameters.Add("@signature",  SqliteType.Text);
            var uDocComment = updateCmd.Parameters.Add("@docComment", SqliteType.Text);
            var uIsPartial  = updateCmd.Parameters.Add("@isPartial",  SqliteType.Integer);

            using var insertCmd = db.Connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO symbols
                    (class_name, symbol_type, symbol_name, language, file_path,
                     start_line, end_line, start_byte, end_byte, signature, doc_comment, is_partial)
                VALUES
                    (@className, @symbolType, @symbolName, @language, @filePath,
                     @startLine, @endLine, @startByte, @endByte, @signature, @docComment, @isPartial)
                RETURNING id;
                """;
            var iClassName  = insertCmd.Parameters.Add("@className",  SqliteType.Text);
            var iSymbolType = insertCmd.Parameters.Add("@symbolType", SqliteType.Text);
            var iSymbolName = insertCmd.Parameters.Add("@symbolName", SqliteType.Text);
            var iLanguage   = insertCmd.Parameters.Add("@language",   SqliteType.Text);
            var iFilePath   = insertCmd.Parameters.Add("@filePath",   SqliteType.Text);
            var iStartLine  = insertCmd.Parameters.Add("@startLine",  SqliteType.Integer);
            var iEndLine    = insertCmd.Parameters.Add("@endLine",    SqliteType.Integer);
            var iStartByte  = insertCmd.Parameters.Add("@startByte",  SqliteType.Integer);
            var iEndByte    = insertCmd.Parameters.Add("@endByte",    SqliteType.Integer);
            var iSignature  = insertCmd.Parameters.Add("@signature",  SqliteType.Text);
            var iDocComment = insertCmd.Parameters.Add("@docComment", SqliteType.Text);
            var iIsPartial  = insertCmd.Parameters.Add("@isPartial",  SqliteType.Integer);

            foreach (var sym in parseResult.Symbols)
            {
                var key = (sym.SymbolName, sym.SymbolType, sym.ClassName);
                if (existingQueueMap.TryGetValue(key, out var q) && q.Count > 0)
                {
                    var existing = q.Dequeue();
                    sym.Id = existing.Id;
                    matchedOldIds.Add(existing.Id);

                    uId.Value         = existing.Id;
                    uStartLine.Value  = sym.StartLine;
                    uEndLine.Value    = sym.EndLine;
                    uStartByte.Value  = sym.StartByte;
                    uEndByte.Value    = sym.EndByte;
                    uSignature.Value  = (object?)sym.Signature  ?? DBNull.Value;
                    uDocComment.Value = (object?)sym.DocComment ?? DBNull.Value;
                    uIsPartial.Value  = sym.IsPartial ? 1 : 0;
                    updateCmd.ExecuteNonQuery();
                    symbolsUpdated++;
                }
                else
                {
                    iClassName.Value  = (object?)sym.ClassName  ?? DBNull.Value;
                    iSymbolType.Value = sym.SymbolType.ToString();
                    iSymbolName.Value = sym.SymbolName;
                    iLanguage.Value   = sym.Language ?? parser.Language;
                    iFilePath.Value   = relPath;
                    iStartLine.Value  = sym.StartLine;
                    iEndLine.Value    = sym.EndLine;
                    iStartByte.Value  = sym.StartByte;
                    iEndByte.Value    = sym.EndByte;
                    iSignature.Value  = (object?)sym.Signature  ?? DBNull.Value;
                    iDocComment.Value = (object?)sym.DocComment ?? DBNull.Value;
                    iIsPartial.Value  = sym.IsPartial ? 1 : 0;

                    var id = Convert.ToInt64(insertCmd.ExecuteScalar());
                    sym.Id = id;
                    symbolsAdded++;
                }
            }

            // Remove deleted symbols and clean up references
            var removedSymbols = existingSymbols.Where(s => !matchedOldIds.Contains(s.Id)).ToList();
            if (removedSymbols.Count > 0)
            {
                var removedIds = removedSymbols.Select(s => s.Id).ToList();
                symbolsDeleted = removedIds.Count;
                var inClause = string.Join(",", removedIds);

                using var delRefs = db.Connection.CreateCommand();
                delRefs.Transaction = tx;
                delRefs.CommandText = $"DELETE FROM symbol_references WHERE target_symbol_id IN ({inClause}) OR caller_symbol_id IN ({inClause});";
                delRefs.ExecuteNonQuery();

                using var delCs = db.Connection.CreateCommand();
                delCs.Transaction = tx;
                delCs.CommandText = $"UPDATE cross_stack_mappings SET cs_class_id = NULL WHERE cs_class_id IN ({inClause}); UPDATE cross_stack_mappings SET ts_interface_id = NULL WHERE ts_interface_id IN ({inClause}); DELETE FROM cross_stack_mappings WHERE cs_class_id IS NULL AND ts_interface_id IS NULL;";
                delCs.ExecuteNonQuery();

                using var delSyms = db.Connection.CreateCommand();
                delSyms.Transaction = tx;
                delSyms.CommandText = $"DELETE FROM symbols WHERE id IN ({inClause});";
                delSyms.ExecuteNonQuery();
            }

            // Re-create outgoing references originating from this file
            using (var delOutRefs = db.Connection.CreateCommand())
            {
                delOutRefs.Transaction = tx;
                delOutRefs.CommandText = "DELETE FROM symbol_references WHERE file_path = @filePath;";
                delOutRefs.Parameters.AddWithValue("@filePath", relPath);
                delOutRefs.ExecuteNonQuery();
            }

            if (parseResult.References.Count > 0)
            {
                using var insRef = db.Connection.CreateCommand();
                insRef.Transaction = tx;
                insRef.CommandText = """
                    INSERT INTO symbol_references
                        (caller_symbol_id, target_symbol_id, reference_type, file_path, line_number)
                    VALUES (@callerId, @targetId, @refType, @filePath, @lineNumber);
                    """;
                var pCallerId   = insRef.Parameters.Add("@callerId",   SqliteType.Integer);
                var pTargetId   = insRef.Parameters.Add("@targetId",   SqliteType.Integer);
                var pRefType    = insRef.Parameters.Add("@refType",    SqliteType.Text);
                var pFilePathRef = insRef.Parameters.Add("@filePath",   SqliteType.Text);
                var pLineNumber = insRef.Parameters.Add("@lineNumber", SqliteType.Integer);

                var localSymbols = parseResult.Symbols;
                long? ResolveCaller(string callerName)
                {
                    var localMatch = localSymbols.FirstOrDefault(s => s.SymbolName.Equals(callerName, StringComparison.OrdinalIgnoreCase));
                    if (localMatch is not null && localMatch.Id > 0) return localMatch.Id;
                    return localSymbols.Count > 0 ? localSymbols[0].Id : null;
                }

                long? ResolveTarget(string targetName)
                {
                    var localMatch = localSymbols.FirstOrDefault(s => s.SymbolName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (localMatch is not null && localMatch.Id > 0) return localMatch.Id;

                    using var resolveCmd = db.Connection.CreateCommand();
                    resolveCmd.Transaction = tx;
                    resolveCmd.CommandText = "SELECT id FROM symbols WHERE symbol_name = @name COLLATE NOCASE LIMIT 1;";
                    resolveCmd.Parameters.AddWithValue("@name", targetName);
                    var res = resolveCmd.ExecuteScalar();
                    return res is DBNull or null ? null : Convert.ToInt64(res);
                }

                foreach (var r in parseResult.References)
                {
                    var callerId = ResolveCaller(r.CallerSymbolName);
                    var targetId = ResolveTarget(r.TargetSymbolName);
                    if (callerId is null || targetId is null) continue;

                    pCallerId.Value   = callerId.Value;
                    pTargetId.Value   = targetId.Value;
                    pRefType.Value    = r.ReferenceType;
                    pFilePathRef.Value = relPath;
                    pLineNumber.Value = r.LineNumber;
                    insRef.ExecuteNonQuery();
                    refsUpdated++;
                }
            }

            // Cross-stack DTO linking for DTO candidate symbols in this file
            if (string.Equals(parser.Language, "csharp", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sym in parseResult.Symbols.Where(s => s.SymbolType == SymbolType.Class))
                {
                    if (CrossStack.ServiceStackPatterns.IsDtoCandidate(sym.Namespace ?? sym.ClassName, relPath))
                    {
                        long? tsId = null;
                        foreach (var tsName in CrossStack.ServiceStackPatterns.GetCandidateTsNames(sym.SymbolName))
                        {
                            using var tsCmd = db.Connection.CreateCommand();
                            tsCmd.Transaction = tx;
                            tsCmd.CommandText = "SELECT id FROM symbols WHERE symbol_name = @name COLLATE NOCASE AND language = 'typescript' AND symbol_type = 'Interface' LIMIT 1;";
                            tsCmd.Parameters.AddWithValue("@name", tsName);
                            var tsRes = tsCmd.ExecuteScalar();
                            if (tsRes is not null && tsRes is not DBNull)
                            {
                                tsId = Convert.ToInt64(tsRes);
                                break;
                            }
                        }

                        using var csUpsert = db.Connection.CreateCommand();
                        csUpsert.Transaction = tx;
                        csUpsert.CommandText = """
                            INSERT INTO cross_stack_mappings (dto_name, cs_class_id, ts_interface_id)
                            VALUES (@name, @csId, @tsId)
                            ON CONFLICT(dto_name) DO UPDATE SET
                                cs_class_id = excluded.cs_class_id,
                                ts_interface_id = COALESCE(excluded.ts_interface_id, cross_stack_mappings.ts_interface_id);
                            """;
                        csUpsert.Parameters.AddWithValue("@name", sym.SymbolName);
                        csUpsert.Parameters.AddWithValue("@csId", sym.Id);
                        csUpsert.Parameters.AddWithValue("@tsId", (object?)tsId ?? DBNull.Value);
                        csUpsert.ExecuteNonQuery();
                    }
                }
            }
            else if (string.Equals(parser.Language, "typescript", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sym in parseResult.Symbols.Where(s => s.SymbolType == SymbolType.Interface))
                {
                    using var matchCs = db.Connection.CreateCommand();
                    matchCs.Transaction = tx;
                    matchCs.CommandText = "SELECT id, symbol_name FROM symbols WHERE symbol_name = @name COLLATE NOCASE AND language = 'csharp' AND symbol_type = 'Class' LIMIT 1;";
                    matchCs.Parameters.AddWithValue("@name", sym.SymbolName);
                    using var csReader = matchCs.ExecuteReader();
                    if (csReader.Read())
                    {
                        var csId = csReader.GetInt64(0);
                        var csName = csReader.GetString(1);
                        csReader.Close();

                        using var csUpsert = db.Connection.CreateCommand();
                        csUpsert.Transaction = tx;
                        csUpsert.CommandText = """
                            INSERT INTO cross_stack_mappings (dto_name, cs_class_id, ts_interface_id)
                            VALUES (@name, @csId, @tsId)
                            ON CONFLICT(dto_name) DO UPDATE SET
                                cs_class_id = COALESCE(excluded.cs_class_id, cross_stack_mappings.cs_class_id),
                                ts_interface_id = excluded.ts_interface_id;
                            """;
                        csUpsert.Parameters.AddWithValue("@name", csName);
                        csUpsert.Parameters.AddWithValue("@csId", csId);
                        csUpsert.Parameters.AddWithValue("@tsId", sym.Id);
                        csUpsert.ExecuteNonQuery();
                    }
                }
            }

            tx.Commit();
        }

        sw.Stop();
        _logger?.LogInformation("Surgically indexed {FilePath}: {Updated} updated, {Added} added, {Deleted} deleted, {Refs} refs in {ElapsedMs}ms",
            relPath, symbolsUpdated, symbolsAdded, symbolsDeleted, refsUpdated, sw.ElapsedMilliseconds);

        return new FileUpsertResult(
            relPath,
            symbolsUpdated,
            symbolsAdded,
            symbolsDeleted,
            refsUpdated,
            sw.ElapsedMilliseconds);
    }

    public Task<FileDeleteResult> DeleteFileAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string filePath,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var relPath = WorkspacePathNormalizer.NormalizeRelativePath(workspaceRoot, filePath);
        int symbolsDeleted = 0;
        int refsDeleted = 0;

        lock (db)
        {
            using var tx = db.Connection.BeginTransaction();

            using (var countRefCmd = db.Connection.CreateCommand())
            {
                countRefCmd.Transaction = tx;
                countRefCmd.CommandText = """
                    SELECT COUNT(*) FROM symbol_references
                    WHERE file_path = @filePath
                       OR caller_symbol_id IN (SELECT id FROM symbols WHERE file_path = @filePath)
                       OR target_symbol_id IN (SELECT id FROM symbols WHERE file_path = @filePath);
                    """;
                countRefCmd.Parameters.AddWithValue("@filePath", relPath);
                refsDeleted = Convert.ToInt32(countRefCmd.ExecuteScalar() ?? 0);
            }

            using (var countSymCmd = db.Connection.CreateCommand())
            {
                countSymCmd.Transaction = tx;
                countSymCmd.CommandText = "SELECT COUNT(*) FROM symbols WHERE file_path = @filePath;";
                countSymCmd.Parameters.AddWithValue("@filePath", relPath);
                symbolsDeleted = Convert.ToInt32(countSymCmd.ExecuteScalar() ?? 0);
            }

            using (var delRefCmd = db.Connection.CreateCommand())
            {
                delRefCmd.Transaction = tx;
                delRefCmd.CommandText = """
                    DELETE FROM symbol_references
                    WHERE file_path = @filePath
                       OR caller_symbol_id IN (SELECT id FROM symbols WHERE file_path = @filePath)
                       OR target_symbol_id IN (SELECT id FROM symbols WHERE file_path = @filePath);
                    """;
                delRefCmd.Parameters.AddWithValue("@filePath", relPath);
                delRefCmd.ExecuteNonQuery();
            }

            using (var delCsCmd = db.Connection.CreateCommand())
            {
                delCsCmd.Transaction = tx;
                delCsCmd.CommandText = """
                    UPDATE cross_stack_mappings SET cs_class_id = NULL WHERE cs_class_id IN (SELECT id FROM symbols WHERE file_path = @filePath);
                    UPDATE cross_stack_mappings SET ts_interface_id = NULL WHERE ts_interface_id IN (SELECT id FROM symbols WHERE file_path = @filePath);
                    DELETE FROM cross_stack_mappings WHERE cs_class_id IS NULL AND ts_interface_id IS NULL;
                    """;
                delCsCmd.Parameters.AddWithValue("@filePath", relPath);
                delCsCmd.ExecuteNonQuery();
            }

            using (var delSymCmd = db.Connection.CreateCommand())
            {
                delSymCmd.Transaction = tx;
                delSymCmd.CommandText = "DELETE FROM symbols WHERE file_path = @filePath;";
                delSymCmd.Parameters.AddWithValue("@filePath", relPath);
                delSymCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        sw.Stop();
        _logger?.LogInformation("Surgically deleted {FilePath}: {Symbols} symbols, {Refs} refs in {ElapsedMs}ms",
            relPath, symbolsDeleted, refsDeleted, sw.ElapsedMilliseconds);

        return Task.FromResult(new FileDeleteResult(
            relPath,
            symbolsDeleted,
            refsDeleted,
            sw.ElapsedMilliseconds));
    }
}
