using System.Diagnostics;

using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

/// <summary>
/// Publishes one disk plugin that contributes an account login and a client
/// factory for a custom protocol, once for the test class.
/// </summary>
public sealed class PluginAuthenticationFixture : IDisposable
{
    private readonly TemporaryWorkspace _source = new();

    public PluginAuthenticationFixture()
    {
        Published = Path.Combine(_source.Path, "published");
        Publish(Published);
    }

    public string Published { get; }

    /// <summary>Copies the published plugin into <paramref name="pluginsDirectory"/> with its manifest.</summary>
    public void Install(string pluginsDirectory)
    {
        var target = Path.Combine(pluginsDirectory, "AuthFixture");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(Published))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(
            Path.Combine(target, "plugin.json"),
            """
            {
              "assembly": "AuthFixture.dll",
              "type": "AuthFixture.AuthPlugin"
            }
            """);
    }

    public void Dispose() => _source.Dispose();

    private void Publish(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var project = Path.Combine(_source.Path, "src");
        Directory.CreateDirectory(project);
        var crystal = Path.Combine(AppContext.BaseDirectory, "Crystal.dll");
        var contract = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Plugins.dll");
        Assert.True(File.Exists(contract), contract);
        File.WriteAllText(
            Path.Combine(project, "AuthFixture.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>{Path.Combine(_source.Path, "packages")}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Crystal"><HintPath>{crystal}</HintPath></Reference>
                <Reference Include="CrystalCode.Plugins"><HintPath>{contract}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(project, "AuthPlugin.cs"),
            """
            using System.Net;
            using Crystal.Chat;
            using CrystalCode.Plugins;
            using CrystalCode.Plugins.Authentication;
            using CrystalCode.Plugins.Authentication.Interfaces;
            using CrystalCode.Plugins.Clients;
            using CrystalCode.Plugins.Clients.Interfaces;
            using CrystalCode.Plugins.Interfaces;

            namespace AuthFixture;

            public sealed class AuthPlugin : IPlugin
            {
                public string Name => "Auth fixture";

                public PluginContribution Contribute() =>
                    new PluginContribution(clients: [new Factory()])
                        .WithAuthentications([new Auth(), new BadId(), null!]);
            }

            public sealed class BadId : IPluginAuthentication
            {
                public string Id => "1-bad";
                public string? Validate(PluginAccount account) => null;
                public Task LoginAsync(PluginAccount account, IPluginVault vault, IPluginLogin login, CancellationToken cancellationToken) => Task.CompletedTask;
                public DelegatingHandler CreateHandler(PluginAccount account, IPluginVault vault) => new Stamp(vault);
            }

            public sealed class Auth : IPluginAuthentication
            {
                public string Id => "plug-account";

                public string? Validate(PluginAccount account) =>
                    account.BaseUri.Host == "example.test" ? null : "the address is not the plugin's service.";

                public async Task LoginAsync(PluginAccount account, IPluginVault vault, IPluginLogin login, CancellationToken cancellationToken)
                {
                    if (account.Provider == "boom")
                    {
                        throw new InvalidOperationException("secret-token-xyz");
                    }

                    if (account.Provider == "polite")
                    {
                        throw new PluginAuthenticationException("Polite failure.");
                    }

                    await login.ShowAsync("plugin sign-in for " + account.Provider, cancellationToken);
                    var link = new Uri("https://example.test/signin");
                    if (!await login.OpenLinkAsync(link, cancellationToken))
                    {
                        await login.ShowLinkAsync("Open this address:", link, cancellationToken);
                    }

                    await vault.WriteAsync("{\"token\":\"plug-token\"}", cancellationToken);
                }

                public DelegatingHandler CreateHandler(PluginAccount account, IPluginVault vault) => new Stamp(vault);

                public Task<string?> RevokeAsync(PluginAccount account, IPluginVault vault, CancellationToken cancellationToken) =>
                    Task.FromResult<string?>("plugin revoke warning");
            }

            public sealed class Stamp : DelegatingHandler
            {
                private readonly IPluginVault _vault;

                public Stamp(IPluginVault vault) => _vault = vault;

                protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var json = await _vault.ReadAsync(cancellationToken) ?? "{}";
                    request.Headers.TryAddWithoutValidation("X-Plugin-Token", json.Contains("plug-token") ? "plug-token" : "none");
                    return await base.SendAsync(request, cancellationToken);
                }
            }

            public sealed class Factory : IPluginClientFactory
            {
                public bool CanCreate(string protocol) => protocol == "plug-proto";

                public IStreamingChatClient Create(PluginClientRequest request)
                {
                    if (request.Handler is null)
                    {
                        throw new InvalidOperationException("no-handler key=" + request.ApiKey.Length);
                    }

                    var recorder = new Recorder();
                    request.Handler.InnerHandler = recorder;
                    using var http = request.CreateHttpClient(TimeSpan.FromSeconds(5));
                    http.GetAsync("https://example.test/v1/ping").GetAwaiter().GetResult();
                    throw new InvalidOperationException("sent:" + recorder.Token + " key=" + request.ApiKey.Length);
                }
            }

            public sealed class Recorder : HttpMessageHandler
            {
                public string? Token { get; private set; }

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    Token = request.Headers.TryGetValues("X-Plugin-Token", out var values) ? values.Single() : null;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                }
            }
            """);
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        start.ArgumentList.Add("publish");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(outputDirectory);
        using var process = Process.Start(start);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)));
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stdout + stderr);
    }
}
