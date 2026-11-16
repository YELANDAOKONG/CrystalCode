using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Commands;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Home.Settings;

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

    [Fact]
    public void Merge_KeepsConfigFirstAndDropsRepeats()
    {
        var merged = ServerCommand.Merge(
            ["https://a.example.test", "https://b.example.test"],
            ["https://B.example.test", "https://c.example.test"]);

        Assert.Equal(
            ["https://a.example.test", "https://b.example.test", "https://c.example.test"],
            merged);
    }

    [Fact]
    public void Help_ListsTheCorsOptions()
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
        Assert.Contains("--expose-header", help, StringComparison.Ordinal);
        Assert.Contains("--allow-private-network", help, StringComparison.Ordinal);
        Assert.Contains("--space", help, StringComparison.Ordinal);
    }

    [Fact]
    public void WebHelp_ListsServerOptionsButNoApiBase()
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
            config.AddCommand<WebCommand>("web");
        });

        Assert.Equal(0, app.Run(["web", "--help"]));
        var help = writer.ToString();
        foreach (var option in new[]
        {
            "--listen", "--socket", "--tls-cert", "--auth", "--token-file", "--token-stdin", "--allow-host",
            "--allow-origin", "--expose-header", "--allow-private-network", "--exit-on-stdin-close",
            "--workspace", "--space", "--resume", "--prompt-set", "--spa-fallback"
        })
        {
            Assert.Contains(option, help, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--api-base", help, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerHelp_ListsTheApiBase()
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
        Assert.Contains("--api-base", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--spa-fallback", help, StringComparison.Ordinal);
    }

    [Fact]
    public void WebSettings_FixTheApiBaseAndServeTheHomeWebDirectory()
    {
        using var home = new TemporaryDirectory();
        var crystalHome = new CrystalHome(home.Root);
        var configured = new DiskSettingsStore(crystalHome).LoadOrCreate();

        Assert.True(Web().TryResolve(configured, crystalHome, out var apiBase, out var web, out var error));

        Assert.Null(apiBase);
        Assert.Equal(string.Empty, error);
        Assert.Equal(crystalHome.WebDirectory, web!.Root);
        Assert.True(web.SpaFallback);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("OFF", false)]
    public void WebSettings_SpaFallbackFlagOverridesTheConfig(string flag, bool expected)
    {
        using var home = new TemporaryDirectory();
        var crystalHome = new CrystalHome(home.Root);
        var store = new DiskSettingsStore(crystalHome);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            crystalHome.ConfigPath,
            File.ReadAllText(crystalHome.ConfigPath).Replace("{", "{ \"web\": { \"spaFallback\": false },", StringComparison.Ordinal));
        var configured = store.Load();

        Assert.True(Web().TryResolve(configured, crystalHome, out _, out var web, out _));
        Assert.False(web!.SpaFallback);
        Assert.True(Web(flag).TryResolve(configured, crystalHome, out _, out web, out _));
        Assert.Equal(expected, web!.SpaFallback);
    }

    [Fact]
    public void WebSettings_RejectAnUnknownSpaFallbackValue()
    {
        using var home = new TemporaryDirectory();
        var crystalHome = new CrystalHome(home.Root);
        var configured = new DiskSettingsStore(crystalHome).LoadOrCreate();

        Assert.False(Web("maybe").TryResolve(configured, crystalHome, out _, out var web, out var error));

        Assert.Null(web);
        Assert.Contains("on or off", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerSettings_ApiBaseFlagOverridesTheConfig()
    {
        using var home = new TemporaryDirectory();
        var crystalHome = new CrystalHome(home.Root);
        var store = new DiskSettingsStore(crystalHome);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            crystalHome.ConfigPath,
            File.ReadAllText(crystalHome.ConfigPath).Replace("{", "{ \"server\": { \"apiBase\": \"/from-config\" },", StringComparison.Ordinal));
        var configured = store.Load();
        var plain = new ServerSettings { Resume = new FlagValue<string?>() };
        var flagged = new ServerSettings { Resume = new FlagValue<string?>(), ApiBase = "/from-flag" };

        Assert.True(plain.TryResolve(configured, crystalHome, out var fromConfig, out var web, out _));
        Assert.True(flagged.TryResolve(configured, crystalHome, out var fromFlag, out _, out _));

        Assert.Equal("/from-config", fromConfig);
        Assert.Equal("/from-flag", fromFlag);
        Assert.Null(web);
    }

    private static WebSettings Web(string? spaFallback = null) =>
        new() { Resume = new FlagValue<string?>(), SpaFallback = spaFallback };

    private static ServerSettings Settings(string? auth = null, string? tokenFile = null, bool tokenStdin = false) =>
        new()
        {
            Auth = auth,
            TokenFile = tokenFile,
            TokenStdin = tokenStdin,
            Resume = new FlagValue<string?>()
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("crystal-web-settings-").FullName;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
