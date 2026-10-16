namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Which OpenCode-compatible rule files are appended. Crystal
/// <c>instructions.md</c> and <c>.crystal.md</c> are not covered.
/// </summary>
public sealed record RuleFileSettings
{
    public static RuleFileSettings Default { get; } = new(globalEnabled: true, projectEnabled: true);

    public RuleFileSettings(bool globalEnabled, bool projectEnabled)
    {
        Global = globalEnabled;
        Project = projectEnabled;
    }

    public bool Global { get; }

    public bool Project { get; }

    public bool IsDefault => Global && Project;

    public RuleFileSettings WithGlobal(bool enabled) => new(enabled, Project);

    public RuleFileSettings WithProject(bool enabled) => new(Global, enabled);

    /// <summary>
    /// Applies a process <c>--rules</c> value on top of <paramref name="current"/>.
    /// <c>on</c> and <c>off</c> set both sides. Assignments change only the named side.
    /// </summary>
    public static RuleFileSettings ParseLaunch(string text, RuleFileSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("--rules is required.");
        }

        var trimmed = text.Trim();
        if (IsSwitch(trimmed, out var both))
        {
            return new RuleFileSettings(both, both);
        }

        var global = current.Global;
        var project = current.Project;
        var sawGlobal = false;
        var sawProject = false;
        foreach (var raw in trimmed.Split(','))
        {
            var piece = raw.Trim();
            if (piece.Length == 0)
            {
                throw Invalid();
            }

            var separator = piece.IndexOf('=');
            if (separator <= 0 || separator == piece.Length - 1)
            {
                throw Invalid();
            }

            var name = piece[..separator].Trim();
            var value = piece[(separator + 1)..].Trim();
            if (!IsSwitch(value, out var enabled))
            {
                throw Invalid();
            }

            if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                if (sawGlobal)
                {
                    throw Invalid();
                }

                sawGlobal = true;
                global = enabled;
            }
            else if (name.Equals("project", StringComparison.OrdinalIgnoreCase))
            {
                if (sawProject)
                {
                    throw Invalid();
                }

                sawProject = true;
                project = enabled;
            }
            else
            {
                throw Invalid();
            }
        }

        if (!sawGlobal && !sawProject)
        {
            throw Invalid();
        }

        return new RuleFileSettings(global, project);
    }

    private static bool IsSwitch(string text, out bool enabled)
    {
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static ArgumentException Invalid() =>
        new("--rules must be on, off, or global=on|off and project=on|off.");
}
