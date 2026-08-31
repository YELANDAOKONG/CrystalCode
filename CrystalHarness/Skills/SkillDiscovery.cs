using CrystalHarness.Home;

namespace CrystalHarness.Skills;

/// <summary>
/// Finds OpenCode-compatible SKILL.md files from global and project roots.
/// Later sources overwrite earlier ones with the same name.
/// </summary>
public sealed class SkillDiscovery
{
    private readonly CrystalHome _home;
    private readonly string? _userProfile;
    private readonly string? _configDirectory;

    public SkillDiscovery(
        CrystalHome home,
        string? userProfile,
        string? configDirectory)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
        _userProfile = NormalizeRoot(userProfile);
        _configDirectory = NormalizeRoot(configDirectory);
    }

    public static SkillDiscovery Create(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var config = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(userProfile, ".config")
            : xdg.Trim();
        return new SkillDiscovery(home, userProfile, config);
    }

    public static SkillDiscovery Isolated(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return new SkillDiscovery(
            home,
            Path.Combine(home.Root, "profile"),
            Path.Combine(home.Root, "xdg-config"));
    }

    public SkillCatalog Collect(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var skills = new Dictionary<string, SkillInfo>(StringComparer.Ordinal);
        var workspace = Path.GetFullPath(workspaceRoot);
        var ancestors = EnumerateToGitRoot(workspace);

        if (_userProfile is not null)
        {
            ScanExternal(skills, Path.Combine(_userProfile, SkillFiles.ClaudeDirectory));
            ScanExternal(skills, Path.Combine(_userProfile, SkillFiles.AgentsDirectory));
        }

        foreach (var directory in ancestors)
        {
            ScanExternal(skills, Path.Combine(directory, SkillFiles.ClaudeDirectory));
            ScanExternal(skills, Path.Combine(directory, SkillFiles.AgentsDirectory));
        }

        if (_configDirectory is not null)
        {
            ScanConfig(skills, Path.Combine(_configDirectory, SkillFiles.OpenCodeConfigName));
        }

        foreach (var directory in ancestors)
        {
            ScanConfig(skills, Path.Combine(directory, SkillFiles.OpenCodeDirectory));
        }

        if (_userProfile is not null)
        {
            ScanConfig(skills, Path.Combine(_userProfile, SkillFiles.OpenCodeDirectory));
        }

        ScanConfig(skills, _home.Root);
        foreach (var directory in ancestors)
        {
            ScanConfig(skills, Path.Combine(directory, SkillFiles.CrystalDirectory));
        }

        return new SkillCatalog(skills.Values);
    }

    private static void ScanExternal(Dictionary<string, SkillInfo> skills, string root) =>
        ScanTree(skills, Path.Combine(root, SkillFiles.DirectoryName));

    private static void ScanConfig(Dictionary<string, SkillInfo> skills, string root)
    {
        ScanTree(skills, Path.Combine(root, SkillFiles.AlternateDirectoryName));
        ScanTree(skills, Path.Combine(root, SkillFiles.DirectoryName));
    }

    private static void ScanTree(Dictionary<string, SkillInfo> skills, string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(root, SkillFiles.FileName, SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var path in files)
        {
            if (TryLoad(path, out var skill))
            {
                skills[skill.Name] = skill;
            }
        }
    }

    private static bool TryLoad(string path, out SkillInfo skill)
    {
        skill = null!;
        if (!string.Equals(Path.GetFileName(path), SkillFiles.FileName, StringComparison.Ordinal))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (!SkillFrontmatter.TryRead(text, out var frontmatter, out var body)
            || frontmatter is null)
        {
            return false;
        }

        var directory = Path.GetFileName(Path.GetDirectoryName(path));
        if (!string.Equals(directory, frontmatter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        skill = new SkillInfo(frontmatter.Name, frontmatter.Description, path, body);
        return true;
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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);

    private static string? NormalizeRoot(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
