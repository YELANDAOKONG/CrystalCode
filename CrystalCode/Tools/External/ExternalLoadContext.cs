using System.Reflection;
using System.Runtime.Loader;

namespace CrystalCode.Tools.External;

/// <summary>
/// Isolated load context for one tool set assembly. Shared contract types
/// come from the default context.
/// </summary>
internal sealed class ExternalLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _directory;

    public ExternalLoadContext(string assemblyPath)
        : base(isCollectible: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))
            ?? throw new ArgumentException("Assembly path has no directory.", nameof(assemblyPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        if (IsShared(assemblyName))
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (!ExternalPath.IsInside(_directory, full))
        {
            return null;
        }

        if (IsContract(assemblyName.Name))
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }

        return LoadFromAssemblyPath(full);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unmanagedDllName);
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is null)
        {
            return nint.Zero;
        }

        var full = Path.GetFullPath(path);
        if (!ExternalPath.IsInside(_directory, full))
        {
            return nint.Zero;
        }

        return LoadUnmanagedDllFromPath(full);
    }

    private static bool IsContract(string? name) =>
        name is "Crystal" or "Crystal.Tools";

    private static bool IsShared(AssemblyName assemblyName)
    {
        var simple = assemblyName.Name;
        if (IsContract(simple))
        {
            return true;
        }

        if (simple is null)
        {
            return false;
        }

        if (!IsFramework(simple))
        {
            return false;
        }

        foreach (var loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, simple, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFramework(string name) =>
        name == "System"
        || name == "netstandard"
        || name == "mscorlib"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);
}
