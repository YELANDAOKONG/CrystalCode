namespace CrystalCode.Engine.Version;

/// <summary>
/// Plain-text build identity printed by <c>crystal version</c>.
/// </summary>
public static class BuildIdentityText
{
    public static string Format(BuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var rows = new List<(string Label, string Value)>();
        Add(rows, "Crystal Code", identity.ProductRevision);
        Add(rows, "Crystal", identity.LibraryRevision);
        Add(rows, "SDK", identity.SdkVersion);
        Add(rows, "Runtime", identity.Runtime);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var width = rows.Max(static row => row.Label.Length);
        return string.Join(
            Environment.NewLine,
            rows.Select(row => $"{row.Label.PadRight(width)}  {row.Value}"));
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
