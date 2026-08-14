using CodeGraphMcp.Config;
using CodeGraphMcp.Mcp;

string? repoPath = null;
string? dbDir = null;

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--repo" || args[i] == "-r") && i + 1 < args.Length)
    {
        repoPath = args[++i];
    }
    else if (args[i] == "--db-dir" && i + 1 < args.Length)
    {
        dbDir = args[++i];
    }
    else if (!args[i].StartsWith('-') && repoPath == null && args[i] != "serve")
    {
        repoPath = args[i];
    }
}

string? dbPath = repoPath is not null 
    ? StorageConfig.ResolveDbPath(dbDir, repoPath) 
    : dbDir;

await McpServer.RunAsync(repoPath, dbPath);
