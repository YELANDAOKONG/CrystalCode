namespace CrystalCode.Engine.Version;

/// <summary>
/// Git commit stored after the SDK's assembly-version placeholder.
/// The placeholder, such as <c>1.0.0</c>, is not a product version.
/// </summary>
public static class SourceRevision
{
    public const int CommitLength = 40;

    public static string? FromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        {
            return null;
        }

        var separator = informationalVersion.LastIndexOf('+');
        if (separator < 0 || separator == informationalVersion.Length - 1)
        {
            return null;
        }

        var revision = informationalVersion[(separator + 1)..];
        if (revision.Length != CommitLength || !IsHex(revision))
        {
            return null;
        }

        return revision;
    }

    private static bool IsHex(string revision)
    {
        foreach (var character in revision)
        {
            var hex = character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }
}
