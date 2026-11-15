using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class AccountConsoleTests : IDisposable
{
    private readonly DirectoryInfo _home = Directory.CreateTempSubdirectory("crystal-account-");

    [Fact]
    public async Task Login_ExplainsWhenNoAccountProviderIsConfigured()
    {
        var error = new StringWriter();

        var code = await AccountConsole.LoginAsync(
            Settings(null), new StringWriter(), new StringReader(string.Empty), error, CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("No account providers", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_ReportsAnAbsentLoginAsSuccess()
    {
        WriteAccountProvider();
        var output = new StringWriter();

        var code = await AccountConsole.LogoutAsync(
            Settings("account"), output, new StringWriter(), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("had no saved login", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ReportsAnUnregisteredIntegrationWithoutStartingAnything()
    {
        WriteAccountProvider();
        var error = new StringWriter();

        var code = await AccountConsole.LoginAsync(
            Settings("account"), new StringWriter(), new StringReader(string.Empty), error, CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("not registered", error.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _home.Delete(recursive: true);

    private AccountSettings Settings(string? provider) =>
        new() { Provider = provider, Home = _home.FullName };

    private void WriteAccountProvider() =>
        File.WriteAllText(
            Path.Combine(_home.FullName, "providers.json"),
            """
            {
              "account": {
                "type": "authentication",
                "integration": "unregistered",
                "protocol": "responses",
                "baseUri": "https://example.test/v1/",
                "models": { "model-a": { "contextWindow": 64000 } }
              }
            }
            """);
}
