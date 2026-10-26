using CrystalCode.Server.Security;

namespace CrystalCode.Commands;

/// <summary>
/// Resolves the startup token without ever reading it from a flag:
/// <c>CRYSTAL_SERVER_TOKEN</c>, then an owner-only <c>--token-file</c>, then
/// one line on stdin with <c>--token-stdin</c>, otherwise a new random token.
/// </summary>
internal static class ServerTokenSource
{
    public const string EnvironmentVariable = "CRYSTAL_SERVER_TOKEN";

    private const int MinimumLength = 16;

    public static async Task<(string? Token, bool Generated, string Error)> ResolveAsync(
        ServerSettings settings,
        TextReader input,
        Func<string, string?> environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var auth = settings.Auth?.Trim().ToLowerInvariant() ?? "token";
        if (auth == "none")
        {
            return settings.TokenFile is not null || settings.TokenStdin
                ? (null, false, "Do not pass --token-file or --token-stdin with --auth none.")
                : (null, false, string.Empty);
        }

        if (auth != "token")
        {
            return (null, false, "--auth must be token or none.");
        }

        if (settings.TokenFile is not null && settings.TokenStdin)
        {
            return (null, false, "Pass only one of --token-file or --token-stdin.");
        }

        string? token = environment(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token) && settings.TokenFile is { } file)
        {
            if (!File.Exists(file))
            {
                return (null, false, "Token file not found: " + file);
            }

            if (!OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(file) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead
                    | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                return (null, false, "The token file must be readable by its owner only.");
            }

            token = (await File.ReadAllTextAsync(file, cancellationToken)).Trim();
        }
        else if (string.IsNullOrWhiteSpace(token) && settings.TokenStdin)
        {
            token = (await input.ReadLineAsync(cancellationToken))?.Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            if (settings.TokenFile is not null || settings.TokenStdin)
            {
                return (null, false, "The token is empty.");
            }

            return (TokenStore.NewSecret(), true, string.Empty);
        }

        return token.Trim().Length < MinimumLength
            ? (null, false, $"The token must be at least {MinimumLength} characters.")
            : (token.Trim(), false, string.Empty);
    }
}
