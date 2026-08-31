namespace CrystalCode.Home;

/// <summary>
/// Resolves an API key written in config: literal, {env:NAME}, or {file:path}.
/// </summary>
internal static class ApiKeyText
{
    public static bool TryResolve(
        string configured,
        CrystalHome home,
        out string apiKey,
        out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configured);
        ArgumentNullException.ThrowIfNull(home);

        apiKey = string.Empty;
        error = string.Empty;
        var text = configured.Trim();
        if (TryUnwrap(text, "env", out var environmentName))
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"Environment variable {environmentName} is not set.";
                return false;
            }

            apiKey = value.Trim();
            return true;
        }

        if (TryUnwrap(text, "file", out var path))
        {
            var fullPath = Expand(path, home);
            if (!File.Exists(fullPath))
            {
                error = $"API key file not found: {fullPath}.";
                return false;
            }

            var value = File.ReadAllText(fullPath).Trim();
            if (value.Length == 0)
            {
                error = $"API key file is empty: {fullPath}.";
                return false;
            }

            apiKey = value;
            return true;
        }

        apiKey = text;
        return true;
    }

    private static bool TryUnwrap(string text, string kind, out string inner)
    {
        inner = string.Empty;
        var prefix = "{" + kind + ":";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith('}'))
        {
            return false;
        }

        inner = text[prefix.Length..^1].Trim();
        return inner.Length > 0;
    }

    private static string Expand(string path, CrystalHome home)
    {
        var expanded = path.Trim();
        if (expanded == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (expanded.StartsWith("~/", StringComparison.Ordinal)
            || expanded.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[2..]);
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(home.Root, expanded));
    }
}
