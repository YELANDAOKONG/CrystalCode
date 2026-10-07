namespace CrystalCode.Engine.Version;

/// <summary>
/// Plain-text build identity printed by <c>crystal version</c>.
/// A blank line separates the build section from the host section.
/// </summary>
public static class BuildIdentityText
{
    public static string Format(BuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var build = new List<(string Label, string Value)>();
        Add(build, "Crystal Code", identity.ProductRevision);
        Add(build, "Crystal", identity.LibraryRevision);
        Add(build, "SDK", identity.SdkVersion);
        Add(build, "Configuration", identity.Configuration);

        var host = new List<(string Label, string Value)>();
        Add(host, "Runtime", identity.Runtime);
        Add(host, "OS", identity.OperatingSystem);

        var rows = new List<(string Label, string Value)>(build.Count + host.Count);
        rows.AddRange(build);
        rows.AddRange(host);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var width = rows.Max(static row => row.Label.Length);
        var sections = new List<string>(2);
        AddSection(sections, build, width);
        AddSection(sections, host, width);
        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static void AddSection(
        List<string> sections,
        List<(string Label, string Value)> rows,
        int width)
    {
        if (rows.Count == 0)
        {
            return;
        }

        sections.Add(string.Join(
            Environment.NewLine,
            rows.Select(row => $"{row.Label.PadRight(width)}  {row.Value}")));
    }

    private static void Add(List<(string Label, string Value)> rows, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        rows.Add((label, value.Trim()));
    }
}
