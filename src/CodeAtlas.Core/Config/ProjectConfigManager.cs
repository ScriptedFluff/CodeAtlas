using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeAtlas.Core.Config;

/// <summary>
/// Result of an ignore pattern mutation operation.
/// </summary>
public sealed record ConfigMutationResult(
    bool Changed,
    bool AlreadyExisted,
    bool NotFound,
    string NormalizedPattern,
    string SettingsPath,
    string Message);

/// <summary>
/// Manages reading, mutating, and persisting project-level settings in .codeatlas/settings.json.
/// Uses JSON DOM manipulation to preserve existing comments, properties, and indentation.
/// </summary>
public static class ProjectConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string GetSettingsPath(string repoRoot)
    {
        return Path.Combine(repoRoot, ".codeatlas", "settings.json");
    }

    public static string NormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return string.Empty;
        return pattern.Trim().Replace('\\', '/');
    }

    /// <summary>
    /// Reads all active ignore patterns from .codeatlas/settings.json if present.
    /// </summary>
    public static IReadOnlyList<string> GetIgnorePatterns(string repoRoot)
    {
        var path = GetSettingsPath(repoRoot);
        if (!File.Exists(path)) return Array.Empty<string>();

        try
        {
            var text = File.ReadAllText(path);
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (node is not JsonObject obj) return Array.Empty<string>();

            var prop = FindIgnoreProperty(obj);
            if (prop != null && obj[prop] is JsonArray array)
            {
                var list = new List<string>();
                foreach (var item in array)
                {
                    var val = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        list.Add(val);
                    }
                }
                return list;
            }
        }
        catch
        {
            // Ignore parse errors and fallback to empty list
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Adds an ignore pattern to .codeatlas/settings.json, creating the file/directory if necessary.
    /// Deduplicates patterns (case-insensitively) and normalizes slashes.
    /// </summary>
    public static ConfigMutationResult AddIgnorePattern(string repoRoot, string rawPattern)
    {
        var normalized = NormalizePattern(rawPattern);
        var settingsPath = GetSettingsPath(repoRoot);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: false,
                NormalizedPattern: string.Empty,
                SettingsPath: settingsPath,
                Message: "Cannot add empty or whitespace ignore pattern."
            );
        }

        var dir = Path.GetDirectoryName(settingsPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        JsonObject rootObj;
        if (File.Exists(settingsPath))
        {
            try
            {
                var text = File.ReadAllText(settingsPath);
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                rootObj = node as JsonObject ?? new JsonObject();
            }
            catch
            {
                rootObj = new JsonObject();
            }
        }
        else
        {
            rootObj = new JsonObject
            {
                ["$schema"] = "https://raw.githubusercontent.com/ScriptedFluff/CodeAtlas/main/schema.json",
                ["version"] = 1
            };
        }

        var propName = FindIgnoreProperty(rootObj) ?? "ignorePatterns";
        JsonArray targetArray;
        if (rootObj[propName] is JsonArray existingArr)
        {
            targetArray = existingArr;
        }
        else
        {
            targetArray = new JsonArray();
            rootObj[propName] = targetArray;
        }

        // Deduplication check
        foreach (var item in targetArray)
        {
            var val = item?.GetValue<string>();
            if (val != null && val.Trim().Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigMutationResult(
                    Changed: false,
                    AlreadyExisted: true,
                    NotFound: false,
                    NormalizedPattern: normalized,
                    SettingsPath: settingsPath,
                    Message: $"Pattern '{normalized}' is already in ignore list."
                );
            }
        }

        ((IList<JsonNode?>)targetArray).Add(JsonValue.Create(normalized));
        File.WriteAllText(settingsPath, rootObj.ToJsonString(JsonOptions) + Environment.NewLine);

        return new ConfigMutationResult(
            Changed: true,
            AlreadyExisted: false,
            NotFound: false,
            NormalizedPattern: normalized,
            SettingsPath: settingsPath,
            Message: $"Added ignore pattern '{normalized}' to .codeatlas/settings.json"
        );
    }

    /// <summary>
    /// Removes an existing ignore pattern from .codeatlas/settings.json.
    /// </summary>
    public static ConfigMutationResult RemoveIgnorePattern(string repoRoot, string rawPattern)
    {
        var normalized = NormalizePattern(rawPattern);
        var settingsPath = GetSettingsPath(repoRoot);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: true,
                NormalizedPattern: string.Empty,
                SettingsPath: settingsPath,
                Message: "Cannot remove empty or whitespace ignore pattern."
            );
        }

        if (!File.Exists(settingsPath))
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: true,
                NormalizedPattern: normalized,
                SettingsPath: settingsPath,
                Message: $"Pattern '{normalized}' not found in ignore configuration."
            );
        }

        JsonObject rootObj;
        try
        {
            var text = File.ReadAllText(settingsPath);
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (node is not JsonObject obj)
            {
                return new ConfigMutationResult(
                    Changed: false,
                    AlreadyExisted: false,
                    NotFound: true,
                    NormalizedPattern: normalized,
                    SettingsPath: settingsPath,
                    Message: $"Pattern '{normalized}' not found in ignore configuration."
                );
            }
            rootObj = obj;
        }
        catch
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: true,
                NormalizedPattern: normalized,
                SettingsPath: settingsPath,
                Message: $"Could not parse {settingsPath}."
            );
        }

        var propName = FindIgnoreProperty(rootObj);
        if (propName == null || rootObj[propName] is not JsonArray targetArray)
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: true,
                NormalizedPattern: normalized,
                SettingsPath: settingsPath,
                Message: $"Pattern '{normalized}' not found in ignore configuration."
            );
        }

        int matchIndex = -1;
        for (int i = 0; i < targetArray.Count; i++)
        {
            var val = targetArray[i]?.GetValue<string>();
            if (val != null && val.Trim().Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                matchIndex = i;
                break;
            }
        }

        if (matchIndex < 0)
        {
            return new ConfigMutationResult(
                Changed: false,
                AlreadyExisted: false,
                NotFound: true,
                NormalizedPattern: normalized,
                SettingsPath: settingsPath,
                Message: $"Pattern '{normalized}' not found in ignore configuration."
            );
        }

        targetArray.RemoveAt(matchIndex);
        File.WriteAllText(settingsPath, rootObj.ToJsonString(JsonOptions) + Environment.NewLine);

        return new ConfigMutationResult(
            Changed: true,
            AlreadyExisted: false,
            NotFound: false,
            NormalizedPattern: normalized,
            SettingsPath: settingsPath,
            Message: $"Removed pattern '{normalized}' from .codeatlas/settings.json"
        );
    }

    private static string? FindIgnoreProperty(JsonObject obj)
    {
        foreach (var key in new[] { "ignorePatterns", "IgnorePatterns", "ignore", "Ignore" })
        {
            if (obj.ContainsKey(key))
            {
                return key;
            }
        }
        return null;
    }
}
