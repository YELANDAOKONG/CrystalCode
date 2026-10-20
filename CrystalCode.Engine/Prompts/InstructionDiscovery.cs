using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Finds OpenCode-style rule files and returns them as extra instructions.
/// Files are combined from the outermost directory inward, one per directory,
/// so the closest file is the most specific. They never replace Work, Plan,
/// or Review prompts.
/// </summary>
public sealed class InstructionDiscovery
{
    /// <summary>
    /// Budget for the combined rule files. Past this, later files are trimmed
    /// or dropped and a note is appended, so one large file cannot crowd out
    /// the conversation.
    /// </summary>
    public const int MaxInstructionCharacters = 32 * 1024;

    public const string TruncationNote =
        "Only the first 32768 characters of rule files were included; the rest was not sent to the model.";

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

    public IReadOnlyList<string> Collect(string workspaceRoot, RuleFileSettings? ruleFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var rules = ruleFiles ?? RuleFileSettings.Default;
        var parts = new List<string>();
        var remaining = MaxInstructionCharacters;
        var truncated = false;
        if (rules.Global)
        {
            AddFirstExisting(parts, GlobalCandidates(), ref remaining, ref truncated);
        }

        if (rules.Project)
        {
            AddProjectFamily(parts, Path.GetFullPath(workspaceRoot), ref remaining, ref truncated);
        }

        if (truncated)
        {
            parts.Add(TruncationNote);
        }

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

    private static void AddFirstExisting(
        List<string> parts,
        IEnumerable<string> candidates,
        ref int remaining,
        ref bool truncated)
    {
        foreach (var path in candidates)
        {
            if (remaining <= 0)
            {
                truncated = true;
                return;
            }

            if (TryAdd(parts, path, ref remaining, ref truncated))
            {
                return;
            }
        }
    }

    private static void AddProjectFamily(
        List<string> parts,
        string workspaceRoot,
        ref int remaining,
        ref bool truncated)
    {
        foreach (var directory in EnumerateFromGitRoot(workspaceRoot))
        {
            if (remaining <= 0)
            {
                truncated = true;
                return;
            }

            foreach (var fileName in ProjectFileNames)
            {
                if (TryAdd(parts, Path.Combine(directory, fileName), ref remaining, ref truncated))
                {
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFromGitRoot(string start)
    {
        var directories = new List<string>();
        var gitRoot = GitRoot.Find(start);
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

        directories.Reverse();
        return directories;
    }

    private static bool TryAdd(
        List<string> parts,
        string path,
        ref int remaining,
        ref bool truncated)
    {
        if (remaining <= 0)
        {
            truncated = true;
            return false;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (text.Length == 0)
        {
            return false;
        }

        if (text.Length > remaining)
        {
            var cut = text.LastIndexOf('\n', remaining - 1);
            text = text[..(cut > 0 ? cut : remaining)];
            remaining = 0;
            truncated = true;
        }
        else
        {
            remaining -= text.Length;
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
