using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace CodeGraphMcp.Plugins;

/// <summary>
/// Isolated collectible <see cref="AssemblyLoadContext"/> for dynamic language parser plugins.
/// Resolves managed and native unmanaged assemblies isolated from other plugins,
/// while delegating core contracts to the host default ALC.
/// </summary>
public sealed class ParserLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginAssemblyPath;

    public string PluginAssemblyPath => _pluginAssemblyPath;

    public ParserLoadContext(string pluginAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginAssemblyPath), isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
        _pluginAssemblyPath = Path.GetFullPath(pluginAssemblyPath);
        _resolver = new AssemblyDependencyResolver(_pluginAssemblyPath);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic parser loading requires reflection and ALC loading by design.")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share Core abstractions and TreeSitter with host so type identity matches ILanguageParser, Symbol, Node, etc.
        if (string.Equals(assemblyName.Name, "CodeGraphMcp.Core", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assemblyName.Name, "TreeSitter", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}
