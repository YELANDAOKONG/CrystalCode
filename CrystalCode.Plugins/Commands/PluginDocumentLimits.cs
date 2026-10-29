namespace CrystalCode.Plugins.Commands;

/// <summary>
/// Size limits for a <see cref="PluginDocument"/>. Text and counts past a
/// limit are cut, not rejected, so a plugin cannot fail a command by
/// returning more than a front end should draw.
/// </summary>
public static class PluginDocumentLimits
{
    public const int MaximumBlocks = 50;

    public const int MaximumRows = 200;

    public const int MaximumColumns = 8;

    public const int MaximumTextLength = 500;

    internal static string Clip(string? text)
    {
        var value = (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        return value.Length <= MaximumTextLength ? value : value[..(MaximumTextLength - 3)] + "...";
    }

    internal static IReadOnlyList<T> Take<T>(IEnumerable<T> source, out int omitted)
    {
        ArgumentNullException.ThrowIfNull(source);
        var all = source.ToArray();
        omitted = Math.Max(0, all.Length - MaximumRows);
        return all.Length <= MaximumRows ? all : all[..MaximumRows];
    }
}
