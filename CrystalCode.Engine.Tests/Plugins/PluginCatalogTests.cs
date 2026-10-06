using System.Diagnostics;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginCatalogTests
{
    [Fact]
    public void Load_SkipsInvalidManifestAndDisabledSet()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteManifest(home.Home.PluginsDirectory, "Broken", "{");
        WriteManifest(
            home.Home.PluginsDirectory,
            "Quiet",
            """
            {
              "enabled": false,
              "assembly": "Quiet.dll",
              "type": "Quiet.Plugin"
            }
            """);

        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);

        Assert.Empty(catalog.Plugins);
        Assert.Contains(catalog.Notes, note => note.Contains("Broken", StringComparison.Ordinal));
        Assert.DoesNotContain(catalog.Notes, note => note.Contains("Quiet", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_ProjectDirectoryReplacesHome()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteManifest(
            home.Home.PluginsDirectory,
            "Acme",
            """
            {
              "assembly": "missing.dll",
              "type": "Acme.Plugin"
            }
            """);
        WriteManifest(
            Path.Combine(workspace.Path, ".crystal", "plugins"),
            "Acme",
            """
            {
              "enabled": false,
              "assembly": "Acme.dll",
              "type": "Acme.Plugin"
            }
            """);

        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);

        Assert.Empty(catalog.Plugins);
        Assert.Empty(catalog.Notes);
    }

    [Fact]
    public void Load_RefusesHostAssembly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.PluginsDirectory, "HostCopy");
        Directory.CreateDirectory(directory);
        var engine = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Engine.dll");
        File.Copy(engine, Path.Combine(directory, "CrystalCode.Engine.dll"));
        File.WriteAllText(
            Path.Combine(directory, "plugin.json"),
            """
            {
              "assembly": "CrystalCode.Engine.dll",
              "type": "CrystalCode.Engine.Plugins.PluginRegistry"
            }
            """);

        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);

        Assert.Empty(catalog.Plugins);
        Assert.Contains(
            catalog.Notes,
            note => note.Contains("host assemblies", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_RegistersToolAndHookFromAssembly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.PluginsDirectory, "Fixture");
        PublishFixture(directory);
        File.WriteAllText(
            Path.Combine(directory, "plugin.json"),
            """
            {
              "assembly": "FixturePlugin.dll",
              "type": "FixturePlugin.SamplePlugin"
            }
            """);

        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);

        Assert.Empty(catalog.Notes);
        Assert.Single(catalog.Plugins);
        Assert.Equal("sample", catalog.WorkTools[0].Definition.Name);
        Assert.Empty(catalog.PlanTools);
        var output = await catalog.WorkTools[0].InvokeAsync(new Crystal.Tools.ToolCall("1", "sample", "{}"));
        Assert.Equal("sample-ok", output.Text);
        var pipeline = new PluginHookPipeline(catalog.Hooks);
        Assert.Equal("hook-line", pipeline.AppendPrompt("work", string.Empty));
    }

    [Fact]
    public void Parse_AcceptsPluginProtocolToken()
    {
        var protocol = ProviderProtocol.Parse("acme");

        Assert.Equal("acme", protocol.Value);
        Assert.Equal(ProviderProtocol.OpenAI, ProviderProtocol.Parse("openai"));
    }

    private static void WriteManifest(string root, string name, string json)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.json"), json);
    }

    private static void PublishFixture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var source = new TemporaryWorkspace();
        var project = Path.Combine(source.Path, "src");
        Directory.CreateDirectory(project);
        var crystal = Path.Combine(AppContext.BaseDirectory, "Crystal.dll");
        var tools = Path.Combine(AppContext.BaseDirectory, "Crystal.Tools.dll");
        var contract = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Plugins.dll");
        Assert.True(File.Exists(contract), contract);
        File.WriteAllText(
            Path.Combine(project, "FixturePlugin.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>{Path.Combine(source.Path, "packages")}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Crystal"><HintPath>{crystal}</HintPath></Reference>
                <Reference Include="Crystal.Tools"><HintPath>{tools}</HintPath></Reference>
                <Reference Include="CrystalCode.Plugins"><HintPath>{contract}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(project, "SamplePlugin.cs"),
            """
            using System.Text.Json;
            using Crystal.Tools;
            using CrystalCode.Plugins;
            using CrystalCode.Plugins.Hooks;
            using CrystalCode.Plugins.Tools;

            namespace FixturePlugin;

            public sealed class SamplePlugin : IPlugin
            {
                public string Name => "Sample";

                public PluginContribution Contribute() => new(tools: [new SampleTool()], hooks: [new SampleHook()]);
            }

            public sealed class SampleTool : IPluginTool
            {
                public string Name => "sample";
                public PluginToolCatalogs Catalogs => PluginToolCatalogs.Work;
                public ITool Tool { get; } = new Inner();

                private sealed class Inner : ITool
                {
                    public Inner()
                    {
                        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                        Definition = new ToolDefinition("sample", document.RootElement.Clone(), "Sample.");
                    }

                    public ToolDefinition Definition { get; }

                    public ValueTask<ToolOutput> InvokeAsync(
                        ToolCall call,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(new ToolOutput("sample-ok"));
                }
            }

            public sealed class SampleHook : IPluginHook
            {
                public string? AppendPrompt(PluginPrompt prompt) => "hook-line";
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
