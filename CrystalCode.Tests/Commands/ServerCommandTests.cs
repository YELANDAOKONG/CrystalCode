using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class ServerCommandTests
{
    [Fact]
    public async Task Token_ComesFromTheEnvironmentFirst()
    {
        var (token, generated, error) = await ServerTokenSource.ResolveAsync(
            Settings(),
            new StringReader("from-stdin-0123456789"),
            name => name == ServerTokenSource.EnvironmentVariable ? "from-environment-0123" : null,
            CancellationToken.None);

        Assert.Equal("from-environment-0123", token);
        Assert.False(generated);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public async Task Token_IsGeneratedWhenNoSourceIsGiven()
    {
        var (token, generated, error) = await ServerTokenSource.ResolveAsync(
            Settings(),
            TextReader.Null,
            static _ => null,
            CancellationToken.None);

        Assert.True(generated);
        Assert.True(token!.Length >= 40);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public async Task Token_ReadsStdinOrAnOwnerOnlyFile()
    {
        var fromStdin = await ServerTokenSource.ResolveAsync(
            Settings(tokenStdin: true),
            new StringReader("stdin-token-0123456789\nignored"),
            static _ => null,
            CancellationToken.None);
        Assert.Equal("stdin-token-0123456789", fromStdin.Token);

        var path = Path.Combine(Path.GetTempPath(), "crystal-token-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "file-token-0123456789\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                var open = await ServerTokenSource.ResolveAsync(Settings(tokenFile: path), TextReader.Null, static _ => null, CancellationToken.None);
                Assert.Contains("owner only", open.Error, StringComparison.Ordinal);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            var fromFile = await ServerTokenSource.ResolveAsync(Settings(tokenFile: path), TextReader.Null, static _ => null, CancellationToken.None);
            Assert.Equal("file-token-0123456789", fromFile.Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("none", null, false, "")]
    [InlineData("maybe", null, false, "--auth must be token or none.")]
    [InlineData("none", "file", false, "Do not pass --token-file or --token-stdin with --auth none.")]
    [InlineData("token", "file", true, "Pass only one of --token-file or --token-stdin.")]
    public async Task Auth_RejectsConflicts(string auth, string? file, bool stdin, string expected)
    {
        var (token, _, error) = await ServerTokenSource.ResolveAsync(
            Settings(auth, file, stdin),
            TextReader.Null,
            static _ => null,
            CancellationToken.None);

        Assert.Equal(expected, error);
        Assert.Null(token);
    }

    [Fact]
    public async Task Token_RejectsAShortSecret()
    {
        var (_, _, error) = await ServerTokenSource.ResolveAsync(
            Settings(),
            TextReader.Null,
            static _ => "short",
            CancellationToken.None);

        Assert.Contains("at least 16", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_ListsServerAndSessionOptionsWithoutASecretFlag()
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        var app = new CommandApp<RunCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("crystal");
            config.ConfigureConsole(console);
            config.AddCommand<ServerCommand>("server");
        });

        Assert.Equal(0, app.Run(["server", "--help"]));
        var help = writer.ToString();
        Assert.Contains("--listen", help, StringComparison.Ordinal);
        Assert.Contains("--auth", help, StringComparison.Ordinal);
        Assert.Contains("--prompt-set", help, StringComparison.Ordinal);
        Assert.Contains("--resume", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--token ", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--format", help, StringComparison.Ordinal);
    }

    private static ServerSettings Settings(string? auth = null, string? tokenFile = null, bool tokenStdin = false) =>
        new()
        {
            Auth = auth,
            TokenFile = tokenFile,
            TokenStdin = tokenStdin,
            Resume = new FlagValue<string?>()
        };
}
