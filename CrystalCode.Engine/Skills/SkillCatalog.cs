using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Skills;

/// <summary>
/// Skills discovered for one workspace. Lookup is case-sensitive by name.
/// </summary>
public sealed class SkillCatalog
{
    private readonly IReadOnlyDictionary<string, SkillInfo> _byName;
    private readonly IReadOnlyList<string> _readRoots;

    public SkillCatalog(IEnumerable<SkillInfo> skills, IEnumerable<string>? readRoots = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var byName = new Dictionary<string, SkillInfo>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            ArgumentNullException.ThrowIfNull(skill);
            byName[skill.Name] = skill;
        }

        _byName = byName;
        Items = [.. byName.Values.OrderBy(skill => skill.Name, StringComparer.Ordinal)];
        _readRoots = NormalizeRoots(readRoots);
    }

    public static SkillCatalog Empty { get; } = new([]);

    public IReadOnlyList<SkillInfo> Items { get; }

    public int Count => Items.Count;

    public SkillInfo? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _byName.GetValueOrDefault(name.Trim());
    }

    public bool ContainsReadablePath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!TryCanonical(fullPath, out var candidate))
        {
            return false;
        }

        foreach (var root in _readRoots)
        {
            if (IsInside(candidate, root))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string>? readRoots)
    {
        if (readRoots is null)
        {
            return [];
        }

        var roots = new List<string>();
        foreach (var root in readRoots)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            if (!TryCanonical(root, out var full))
            {
                continue;
            }

            if (!roots.Exists(existing => string.Equals(existing, full, StringComparison.Ordinal)))
            {
                roots.Add(full);
            }
        }

        return roots;
    }

    private static bool IsInside(string fullPath, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            || string.Equals(fullPath, root, StringComparison.Ordinal);
    }

    private static bool TryCanonical(string path, out string full)
    {
        try
        {
            full = Workspace.Canonicalize(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
        {
            full = string.Empty;
            return false;
        }
    }
}
