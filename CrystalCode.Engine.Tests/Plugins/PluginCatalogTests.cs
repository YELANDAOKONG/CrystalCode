using System.Diagnostics;

using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Hooks;

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

        var approval = "audit";
        var root = new Workspace(workspace.Path);
        var host = new SessionToolHost(root, () => "sess-1", () => approval);
        var catalog = PluginCatalog.Load(home.Home, root, enabled: true, host);

        Assert.Equal("Plugin 'Fixture' registered a raw hook.", Assert.Single(catalog.Notes));
        var info = Assert.Single(catalog.Plugins);
        Assert.Equal(1, info.Hooks);
        Assert.Equal(1, info.RawHooks);
        Assert.Single(catalog.RawHooks);
        Assert.Equal("sample", catalog.WorkTools[0].Definition.Name);
        Assert.Empty(catalog.PlanTools);
        var output = await catalog.WorkTools[0].InvokeAsync(new Crystal.Tools.ToolCall("1", "sample", "{}"));
        Assert.Equal("sample-ok", output.Text);
        var hosted = catalog.WorkTools.Single(tool => tool.Definition.Name == "hosted");
        var first = await hosted.InvokeAsync(new Crystal.Tools.ToolCall("2", "hosted", "{}"));
        Assert.Equal(root.Root + "\nsess-1\naudit", first.Text);
        approval = "full";
        var second = await hosted.InvokeAsync(new Crystal.Tools.ToolCall("3", "hosted", "{}"));
        Assert.Equal(root.Root + "\nsess-1\nfull", second.Text);
        var pipeline = new PluginHookPipeline(catalog.Hooks, rawHooks: catalog.RawHooks);
        Assert.Equal("hook-line", pipeline.OnPrompt("work", string.Empty));
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);
        var added = Assert.IsType<ChatMessage>(sent[^1]);
        Assert.Equal(ChatRole.User, added.Role);
        Assert.Equal("raw-line", added.Text);
        Assert.Equal("sample_token", Assert.Single(catalog.Placeholders).Name);
        var environment = new PluginEnvironment(
            [new PluginPeer("Fixture", "home", "Sample", true, true, true, string.Empty)],
            [new ExternalToolSetPeer("Extra", "project", true, true, false, string.Empty)],
            [new ExternalToolPeer("lint", "Extra", "project", plan: false, work: true)],
            [new SkillPeer("review-diff", "Review the diff.")]);
        catalog.Attach(environment, _ => { });
        var peers = catalog.WorkTools.Single(tool => tool.Definition.Name == "peers");
        var listed = await peers.InvokeAsync(new Crystal.Tools.ToolCall("4", "peers", "{}"));
        Assert.Equal("1\n1\n1\n1", listed.Text);
        var table = new PluginPlaceholderTable(catalog.Placeholders, _ => { });
        table.SetEnvironment(environment);
        var bound = PromptBinder.Apply(
            "{{sample_token}}",
            PromptContext.Create(
                root.Root,
                "openai",
                "gpt",
                "work",
                string.Empty,
                string.Empty,
                sessionId: "sess-1",
                approval: "audit"),
            table);
        Assert.Equal("token:1:work", bound);
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
        var hostTools = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Tools.dll");
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
                <Reference Include="CrystalCode.Tools"><HintPath>{hostTools}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(project, "SamplePlugin.cs"),
            """
            using System.Text.Json;
            using Crystal.Tools;
            using CrystalCode.Plugins;
            using CrystalCode.Plugins.Environment;
            using CrystalCode.Plugins.Hooks;
            using CrystalCode.Plugins.Placeholders;
            using CrystalCode.Plugins.Tools;
            using CrystalCode.Tools;

            namespace FixturePlugin;

            public sealed class SamplePlugin : IPlugin
            {
                private IPluginEnvironment _environment = PluginEnvironment.Empty;

                public string Name => "Sample";

                public IPluginEnvironment Environment => _environment;

                public void Attach(IPluginEnvironment environment) => _environment = environment;

                public PluginContribution Contribute() => new(
                    tools: [new SampleTool(), new HostedTool(), new PeerTool(this)],
                    hooks: [new SampleHook()],
                    rawHooks: [new SampleRawHook()],
                    placeholders: [new SamplePlaceholder()]);
            }

            public sealed class SamplePlaceholder : IPluginPlaceholder
            {
                public string Name => "sample_token";

                public string Resolve(PluginPlaceholderContext context) =>
                    "token:" + context.Environment.Plugins.Count + ":" + context.Mode;
            }

            public sealed class PeerTool : IPluginTool
            {
                private readonly SamplePlugin _plugin;

                public PeerTool(SamplePlugin plugin) => _plugin = plugin;

                public string Name => "peers";

                public PluginToolCatalogs Catalogs => PluginToolCatalogs.Work;

                public ITool Tool => new Inner(_plugin);

                private sealed class Inner : ITool
                {
                    private readonly SamplePlugin _plugin;

                    public Inner(SamplePlugin plugin)
                    {
                        _plugin = plugin;
                        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                        Definition = new ToolDefinition("peers", document.RootElement.Clone(), "Peers.");
                    }

                    public ToolDefinition Definition { get; }

                    public ValueTask<ToolOutput> InvokeAsync(
                        ToolCall call,
                        CancellationToken cancellationToken = default)
                    {
                        var environment = _plugin.Environment;
                        return ValueTask.FromResult(new ToolOutput(
                            environment.Plugins.Count
                            + "\n" + environment.ToolSets.Count
                            + "\n" + environment.ExternalTools.Count
                            + "\n" + environment.Skills.Count));
                    }
                }
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

            public sealed class HostedTool : IPluginTool
            {
                public string Name => "hosted";
                public PluginToolCatalogs Catalogs => PluginToolCatalogs.Work;
                public ITool Tool { get; } = new Inner();

                private sealed class Inner : IHostTool
                {
                    public Inner()
                    {
                        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                        Definition = new ToolDefinition("hosted", document.RootElement.Clone(), "Hosted.");
                    }

                    public ToolDefinition Definition { get; }

                    public ValueTask<ToolOutput> InvokeAsync(
                        ToolCall call,
                        ToolHostContext context,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(new ToolOutput(
                            context.WorkspaceRoot + "\n" + context.SessionId + "\n" + context.Approval));
                }
            }

            public sealed class SampleHook : IPluginHook
            {
                public string? OnPrompt(PluginPrompt prompt) => "hook-line";
            }

            public sealed class SampleRawHook : IPluginRawHook
            {
                public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
                    PluginModelRequest request,
                    CancellationToken cancellationToken = default)
                {
                    IReadOnlyList<PluginModelItem> next =
                    [
                        .. request.Items,
                        new PluginModelMessage("raw-1", Crystal.Chat.ChatRole.User, "raw-line")
                    ];
                    return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
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
