using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class AccountConsoleTests : IDisposable
{
    private readonly DirectoryInfo _home = Directory.CreateTempSubdirectory("crystal-account-");

    [Fact]
    public async Task Login_WithoutAProviderListsAccountsAndSignsInNothing()
    {
        WriteAccountProvider();
        var output = new StringWriter();

        var code = await AccountConsole.LoginAsync(
            Settings(null, "text"), output, new StringReader(string.Empty), new StringWriter(), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal("account\tunregistered\tsigned out" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Login_WithoutAnyAccountProviderSaysSo()
    {
        var output = new StringWriter();

        var code = await AccountConsole.LoginAsync(
            Settings(null), output, new StringReader(string.Empty), new StringWriter(), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("No account providers", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_RejectsAnythingButText()
    {
        var error = new StringWriter();

        var code = await AccountConsole.LogoutAsync(
            Settings(null, "json"), new StringWriter(), error, CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("Format must be text", error.ToString(), StringComparison.Ordinal);
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

    private AccountSettings Settings(string? provider, string? format = null) =>
        new() { Provider = provider, Home = _home.FullName, Format = format };

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
