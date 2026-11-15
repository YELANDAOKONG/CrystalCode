using CrystalCode.Authentication.ChatGpt;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Authentication;

/// <summary>
/// Registers this assembly's authentication integrations with the host. It
/// ships no provider definitions: an operator declares each account provider,
/// with its models, in <c>providers.json</c>.
/// </summary>
public static class AuthenticationHost
{
    public const string Key = "authentication";

    public static HostContribution CreateContribution() => new(
        Key,
        plugins: [static () => new ChatGptPlugin()]);

    public static void Register() => HostDefaults.Register(CreateContribution());
}
