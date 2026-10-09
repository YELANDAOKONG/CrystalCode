using System.Diagnostics;

using Crystal.Chat;
using Crystal.Multimodal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Plugins.Clients;
using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Models;

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
        var host = new SessionToolHost(root, home.Home, () => "sess-1", () => approval);
        var catalog = PluginCatalog.Load(home.Home, root, enabled: true, host);

        Assert.Contains(
            "Plugin 'Fixture' registered a raw hook.",
            catalog.Notes);
        Assert.Contains(
            "Plugin 'Fixture' can call the session and review models.",
            catalog.Notes);
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
        catalog.AttachSession(
            new PluginModels(
                new PluginModel(
                    "openai",
                    "openai",
                    "gpt",
                    contextWindow: 8,
                    maxTokens: null,
                    temperature: null,
                    topP: null,
                    imageInput: false,
                    thinking: "low"),
                PluginReview.UsingSession),
            _ => { });
        catalog.AttachClients(new IdleClients(), _ => { });
        catalog.AttachDataDirectories(home.Home, root.Root, _ => { });
        var dataPaths = ExtensionDataPaths.Resolve(
            home.Home,
            root.Root,
            ExtensionDataKind.Plugins,
            "Fixture");
        var dataTool = catalog.WorkTools.Single(tool => tool.Definition.Name == "datadirs");
        var dataLine = await dataTool.InvokeAsync(new Crystal.Tools.ToolCall("5", "datadirs", "{}"));
        Assert.Equal(dataPaths.GlobalDirectory + "\n" + dataPaths.ProjectDirectory, dataLine.Text);
        Assert.True(Directory.Exists(dataPaths.GlobalDirectory));
        Assert.True(Directory.Exists(dataPaths.ProjectDirectory));
        var peers = catalog.WorkTools.Single(tool => tool.Definition.Name == "peers");
        var listed = await peers.InvokeAsync(new Crystal.Tools.ToolCall("4", "peers", "{}"));
        Assert.Equal("1\n1\n1\n1\nsession\nyes", listed.Text);
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
            using CrystalCode.Plugins.Clients;
            using CrystalCode.Plugins.Data;
            using CrystalCode.Plugins.Environment;
            using CrystalCode.Plugins.Hooks;
            using CrystalCode.Plugins.Models;
            using CrystalCode.Plugins.Placeholders;
            using CrystalCode.Plugins.Tools;
            using CrystalCode.Tools;

            namespace FixturePlugin;

            public sealed class SamplePlugin : IPlugin, IPluginModelClient, IPluginDataDirectory
            {
                private IPluginEnvironment _environment = PluginEnvironment.Empty;
                private IPluginModels _models = PluginModels.Empty;
                private IPluginClients? _clients;
                private string _globalDataDirectory = string.Empty;
                private string _projectDataDirectory = string.Empty;

                public string Name => "Sample";

                public IPluginEnvironment Environment => _environment;

                public IPluginModels Models => _models;

                public IPluginClients? Clients => _clients;

                public string DataLine => _globalDataDirectory + "\n" + _projectDataDirectory;

                public void Attach(IPluginEnvironment environment) => _environment = environment;

                public void AttachSession(IPluginModels models) => _models = models;

                public void AttachClients(IPluginClients clients) => _clients = clients;

                public void AttachDataDirectories(
                    string globalDataDirectory,
                    string projectDataDirectory)
                {
                    _globalDataDirectory = globalDataDirectory;
                    _projectDataDirectory = projectDataDirectory;
                }

                public PluginContribution Contribute() => new(
                    tools: [new SampleTool(), new HostedTool(), new DataTool(this), new PeerTool(this)],
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
                        var review = _plugin.Models.Review.Independent ? "own" : "session";
                        var clients = _plugin.Clients is null ? "no" : "yes";
                        return ValueTask.FromResult(new ToolOutput(
                            environment.Plugins.Count
                            + "\n" + environment.ToolSets.Count
                            + "\n" + environment.ExternalTools.Count
                            + "\n" + environment.Skills.Count
                            + "\n" + review
                            + "\n" + clients));
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

            public sealed class DataTool : IPluginTool
            {
                private readonly SamplePlugin _plugin;

                public DataTool(SamplePlugin plugin) => _plugin = plugin;

                public string Name => "datadirs";

                public PluginToolCatalogs Catalogs => PluginToolCatalogs.Work;

                public ITool Tool => new Inner(_plugin);

                private sealed class Inner : ITool
                {
                    private readonly SamplePlugin _plugin;

                    public Inner(SamplePlugin plugin)
                    {
                        _plugin = plugin;
                        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                        Definition = new ToolDefinition(
                            "datadirs",
                            document.RootElement.Clone(),
                            "Data directories.");
                    }

                    public ToolDefinition Definition { get; }

                    public ValueTask<ToolOutput> InvokeAsync(
                        ToolCall call,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(new ToolOutput(_plugin.DataLine));
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

    private sealed class IdleClients : IPluginClients
    {
        public bool IndependentReview => false;

        public IStreamingChatClient Session =>
            throw new NotSupportedException("This test does not call the session model.");

        public IStreamingMultimodalChatClient? Images => null;

        public IStreamingChatClient? Review => null;
    }
}
