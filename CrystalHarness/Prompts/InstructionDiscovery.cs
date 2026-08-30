using CrystalHarness.Home;

namespace CrystalHarness.Prompts;

/// <summary>
/// Finds OpenCode-style rule files and returns them as extra instructions.
/// Files are combined. They never replace Work, Plan, or Review prompts.
/// </summary>
public sealed class InstructionDiscovery
{
    private static readonly string[] ProjectFileNames =
    [
        InstructionNames.Agents,
        InstructionNames.Claude,
        InstructionNames.Context
    ];

    private readonly CrystalHome _home;
    private readonly string? _userProfile;
    private readonly string? _configDirectory;

    public InstructionDiscovery(
        CrystalHome home,
        string? userProfile,
        string? configDirectory)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
        _userProfile = NormalizeRoot(userProfile);
        _configDirectory = NormalizeRoot(configDirectory);
    }

    public static InstructionDiscovery Create(CrystalHome home)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var config = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(userProfile, ".config")
            : xdg.Trim();
        return new InstructionDiscovery(home, userProfile, config);
    }

    public static InstructionDiscovery Isolated(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return new InstructionDiscovery(
            home,
            Path.Combine(home.Root, "profile"),
            Path.Combine(home.Root, "xdg-config"));
    }

    public IReadOnlyList<string> Collect(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var parts = new List<string>();
        AddFirstExisting(parts, GlobalCandidates());
        AddProjectFamily(parts, Path.GetFullPath(workspaceRoot));
        return parts;
    }

    private IEnumerable<string> GlobalCandidates()
    {
        yield return Path.Combine(_home.Root, InstructionNames.Agents);
        yield return Path.Combine(_home.Root, InstructionNames.Claude);
        if (_configDirectory is not null)
        {
            yield return Path.Combine(_configDirectory, "opencode", InstructionNames.Agents);
        }

        if (_userProfile is not null)
        {
            yield return Path.Combine(_userProfile, ".claude", InstructionNames.Claude);
        }
    }

    private static void AddFirstExisting(List<string> parts, IEnumerable<string> candidates)
    {
        foreach (var path in candidates)
        {
            if (TryAdd(parts, path))
            {
                return;
            }
        }
    }

    private static void AddProjectFamily(List<string> parts, string workspaceRoot)
    {
        var directories = EnumerateToGitRoot(workspaceRoot);
        foreach (var fileName in ProjectFileNames)
        {
            var added = false;
            foreach (var directory in directories)
            {
                if (TryAdd(parts, Path.Combine(directory, fileName)))
                {
                    added = true;
                }
            }

            if (added)
            {
                return;
            }
        }
    }

    private static IReadOnlyList<string> EnumerateToGitRoot(string start)
    {
        var directories = new List<string>();
        var gitRoot = FindGitRoot(start);
        var current = start;
        while (true)
        {
            directories.Add(current);
            if (gitRoot is null || PathsEqual(current, gitRoot))
            {
                break;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return directories;
    }

    private static string? FindGitRoot(string start)
    {
        var current = start;
        while (true)
        {
            var git = Path.Combine(current, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                return null;
            }

            current = parent.FullName;
        }
    }

    private static bool TryAdd(List<string> parts, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        parts.Add("Instructions from: " + Path.GetFullPath(path) + "\n" + text);
        return true;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);

    private static string? NormalizeRoot(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
