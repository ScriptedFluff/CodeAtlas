using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAtlas.Core.Config;

/// <summary>
/// Cross-platform installation beacon record persisted to <c>install.json</c>
/// so companion tools (such as <c>CodeAtlas.Traits</c>) can locate the active CLI executable.
/// </summary>
public sealed record InstallBeaconRecord(
    string CliExecutable,
    string BaseDirectory,
    string Version,
    string LastSeenUtc);

[JsonSerializable(typeof(InstallBeaconRecord))]
internal sealed partial class InstallBeaconJsonContext : JsonSerializerContext { }

/// <summary>
/// Manages writing and reading the cross-platform <c>install.json</c> discovery beacon
/// under the user's local application data folder (<c>&lt;LocalApplicationData&gt;/CodeAtlas/install.json</c>).
/// </summary>
public static class InstallBeacon
{
    public const string BeaconFileName = "install.json";
    public const string AppFolderName  = "CodeAtlas";

    /// <summary>
    /// Resolves the cross-platform path to <c>install.json</c>.
    /// </summary>
    public static string GetBeaconFilePath(string? customAppDataDirectory = null)
    {
        // ── 1. Declarations ──
        string baseDirectory;
        string resultPath;

        // ── 2. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(customAppDataDirectory))
        {
            return Path.Combine(Path.GetFullPath(customAppDataDirectory), AppFolderName, BeaconFileName);
        }

        // ── 3. Intent-First Narrative ──
        baseDirectory = ResolveUserDataRoot();
        resultPath    = Path.Combine(baseDirectory, AppFolderName, BeaconFileName);

        // ── 4. Output / Return ──
        return resultPath;
    }

    /// <summary>
    /// Best-effort registration of the currently running CodeAtlas binary location.
    /// Never throws exceptions so CLI and MCP startup are never disrupted.
    /// </summary>
    public static void TryRegisterCurrentProcessQuietly(string? customAppDataDirectory = null)
    {
        try
        {
            RegisterCurrentProcess(customAppDataDirectory);
        }
        catch
        {
            // Best-effort beacon write; never block CLI/MCP execution.
        }
    }

    /// <summary>
    /// Writes the installation beacon for the current process directory.
    /// </summary>
    public static InstallBeaconRecord? RegisterCurrentProcess(string? customAppDataDirectory = null)
    {
        // ── 1. Declarations ──
        string baseDir;
        string? cliExecutable;
        string version;
        string beaconPath;
        InstallBeaconRecord record;

        // ── 2. Preconditions & Guard Clauses ──
        baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
        {
            return null;
        }

        cliExecutable = ResolveCliExecutableInDirectory(baseDir);
        if (string.IsNullOrWhiteSpace(cliExecutable))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        version    = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        beaconPath = GetBeaconFilePath(customAppDataDirectory);
        record     = new InstallBeaconRecord(
            CliExecutable: cliExecutable,
            BaseDirectory: Path.GetFullPath(baseDir),
            Version: version,
            LastSeenUtc: DateTime.UtcNow.ToString("O"));

        WriteBeaconAtomically(beaconPath, record);

        // ── 4. Output / Return ──
        return record;
    }

    private static string ResolveUserDataRoot()
    {
        // ── 1. Declarations ──
        string localAppData;
        string userProfile;

        // ── 2. Preconditions & Guard Clauses ──
        localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return localAppData;
        }

        // ── 3. Intent-First Narrative ──
        userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ── 4. Output / Return ──
        return Path.Combine(userProfile, ".local", "share");
    }

    private static string? ResolveCliExecutableInDirectory(string baseDir)
    {
        // ── 1. Declarations ──
        string[] candidateNames;

        // ── 2. Preconditions & Guard Clauses ──
        if (!Directory.Exists(baseDir))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        candidateNames = OperatingSystem.IsWindows()
            ? ["CodeAtlas.exe", "CodeAtlas.Cli.exe", "CodeAtlas.dll"]
            : ["CodeAtlas", "CodeAtlas.Cli", "CodeAtlas.dll"];

        foreach (var name in candidateNames)
        {
            var candidatePath = Path.Combine(baseDir, name);
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        // ── 4. Output / Return ──
        return Environment.ProcessPath;
    }

    private static void WriteBeaconAtomically(string beaconPath, InstallBeaconRecord record)
    {
        // ── 1. Declarations ──
        string? parentDir;
        string jsonPayload;

        // ── 2. Preconditions & Guard Clauses ──
        parentDir = Path.GetDirectoryName(beaconPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        // ── 3. Intent-First Narrative ──
        jsonPayload = JsonSerializer.Serialize(record, InstallBeaconJsonContext.Default.InstallBeaconRecord);
        File.WriteAllText(beaconPath, jsonPayload);
    }
}
