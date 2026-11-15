using System.Net;
using System.Net.Sockets;

using CrystalCode.Authentication.ChatGpt;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

namespace CrystalCode.Authentication.Tests.ChatGpt;

/// <summary>
/// A temporary home, a vault for one provider, and an integration wired to a
/// fake authorization server.
/// </summary>
internal sealed class ChatGptFixture : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("crystal-auth-");

    public ChatGptFixture(ManualClock? clock = null, string providerName = "chatgpt-test")
    {
        Clock = clock ?? new ManualClock();
        Home = new CrystalHome(_directory.FullName);
        Store = new AuthenticationCredentialStore(Home);
        Server = new FakeAuthorizationServer();
        Provider = CreateProvider(providerName);
        Vault = Store.Open(Provider);
        Integration = new ChatGptIntegration(Server, Clock, FreePort(), HostIdentity.Create("machine", "user"));
    }

    public ManualClock Clock { get; }

    public CrystalHome Home { get; }

    public AuthenticationCredentialStore Store { get; }

    public FakeAuthorizationServer Server { get; }

    public ProviderDefinition Provider { get; }

    public IAuthenticationVault Vault { get; }

    public ChatGptIntegration Integration { get; }

    public static ProviderDefinition CreateProvider(
        string name,
        Uri? baseUri = null,
        IReadOnlyDictionary<string, ModelSettings>? models = null,
        ProviderProtocol? protocol = null) =>
        new(
            ProviderName.Parse(name),
            protocol ?? ProviderProtocol.Responses,
            baseUri ?? new Uri("https://api.openai.com/v1/"),
            models ?? new Dictionary<string, ModelSettings> { ["model-a"] = new(64000) },
            type: ProviderType.Authentication,
            integration: "chatgpt");

    public async Task SignInAsync(ScriptedLoginInteraction? interaction = null)
    {
        interaction ??= new ScriptedLoginInteraction(Server);
        await Integration.LoginAsync(Provider, Vault, interaction, CancellationToken.None);
    }

    public async Task<ChatGptCredentialRecord> ReadRecordAsync() =>
        ChatGptCredentialRecord.Parse((await Vault.ReadAsync(CancellationToken.None))!);

    public string RawFile() => File.ReadAllText(Home.AuthenticationCredentialsPath);

    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
