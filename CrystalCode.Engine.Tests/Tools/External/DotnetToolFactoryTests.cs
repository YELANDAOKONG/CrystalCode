using System.Diagnostics;
using System.Text.Json;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class DotnetToolFactoryTests
{
    [Fact]
    public async Task Load_DotnetAssembly_RegistersEveryPublicTool()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, ".crystal", "tools", "FixtureTools");
        PublishFixture(output);
        File.WriteAllText(
            Path.Combine(output, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll"
            }
            """);

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        Assert.NotNull(catalog.WorkTools.FirstOrDefault(tool => tool.Definition.Name == "alpha"));
        Assert.NotNull(catalog.WorkTools.FirstOrDefault(tool => tool.Definition.Name == "beta"));
        Assert.True(catalog.WorkTools[0] is ITool);
        var alpha = catalog.WorkTools.First(tool => tool.Definition.Name == "alpha");
        var outputText = await alpha.InvokeAsync(new ToolCall("1", "alpha", "{}"));
        Assert.Equal("alpha-ok", outputText.Text);
    }

    [Fact]
    public async Task Load_DotnetTimeoutAndUnlimited_ApplyToCalls()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "SlowTools");
        PublishFixture(
            directory,
            """
            using System.Text.Json;
            using Crystal.Tools;

            namespace FixtureTools;

            public sealed class SlowTool : ITool
            {
                public SlowTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("slow", document.RootElement.Clone(), "Slow.");
                }

                public ToolDefinition Definition { get; }

                public async ValueTask<ToolOutput> InvokeAsync(
                    ToolCall call,
                    CancellationToken cancellationToken = default)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    return new ToolOutput("finished");
                }
            }
            """);
        var manifest = Path.Combine(directory, ExternalFiles.FileName);
        File.WriteAllText(
            manifest,
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "types": ["SlowTool"],
              "timeoutSeconds": 1
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);
        var tool = Assert.Single(catalog.WorkTools);

        var timedOut = await tool.InvokeAsync(new ToolCall("1", "slow", "{}"));

        Assert.Equal(ToolResultStatus.Failure, timedOut.Status);
        Assert.Contains("timed out after 1 second", timedOut.Text, StringComparison.Ordinal);

        File.WriteAllText(
            manifest,
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "types": ["SlowTool"],
              "timeoutSeconds": "unlimited"
            }
            """);
        catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);
        tool = Assert.Single(catalog.WorkTools);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.InvokeAsync(new ToolCall("2", "slow", "{}"), cancellation.Token).AsTask());
    }

    [Fact]
    public void Load_DotnetOverlayMismatch_DoesNotOccupyNames()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, ".crystal", "tools", "FixtureTools");
        PublishFixture(output);
        File.WriteAllText(
            Path.Combine(output, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "tools": {
                "missing_overlay": {}
              }
            }
            """);
        // ZetaStandin sorts after FixtureTools on both ordinal and Windows
        // ignore-case overlay order, so the failed set is applied first.
        WriteExecSet(workspace.Path, "ZetaStandin", "alpha");

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Contains(
            catalog.Notes,
            note => note.Contains("missing_overlay", StringComparison.Ordinal));
        Assert.DoesNotContain(
            catalog.Notes,
            note => note.Contains("already registered", StringComparison.Ordinal));
        Assert.NotNull(catalog.WorkTools.FirstOrDefault(tool => tool.Definition.Name == "alpha"));
        Assert.Null(catalog.WorkTools.FirstOrDefault(tool => tool.Definition.Name == "beta"));
    }

    [Fact]
    public void Load_DotnetConstructorThrows_SkipsSet()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, ".crystal", "tools", "BoomTools");
        PublishFixture(
            output,
            """
            using Crystal.Tools;
            using System.Text.Json;

            namespace FixtureTools;

            public sealed class BoomTool : ITool
            {
                public BoomTool()
                {
                    throw new InvalidOperationException("constructor failed");
                }

                public ToolDefinition Definition { get; } = CreateDefinition();

                public ValueTask<ToolOutput> InvokeAsync(
                    ToolCall call,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(new ToolOutput("boom"));

                private static ToolDefinition CreateDefinition()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    return new ToolDefinition("boom", document.RootElement.Clone(), "Boom.");
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(output, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll"
            }
            """);

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Contains(
            catalog.Notes,
            note => note.Contains("could not be created", StringComparison.Ordinal)
                && note.Contains("constructor failed", StringComparison.Ordinal));
        Assert.Empty(catalog.WorkTools);
        Assert.Empty(catalog.PlanTools);
    }

    [Fact]
    public async Task Load_DotnetMultimodalTool_RegistersNativeToolOnlyInMultimodalCatalog()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, ".crystal", "tools", "ImageTools");
        PublishFixture(
            output,
            """
            using System.Text.Json;

            using Crystal.Media;
            using Crystal.Multimodal;
            using Crystal.Multimodal.Tools;
            using Crystal.Tools;

            namespace FixtureTools;

            public sealed class ImageTool : IMultimodalTool
            {
                public ImageTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}");
                    Definition = new ToolDefinition("image", document.RootElement.Clone(), "Image.");
                }

                public ToolDefinition Definition { get; }

                public ValueTask<MultimodalToolOutput> InvokeAsync(
                    MultimodalToolCall call,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(
                        new MultimodalToolOutput(
                        [
                            new TextContent(call.Arguments),
                            new ImageContent(
                                new ImageMedia(
                                    new InlineMediaSource(new byte[] { 1, 2, 3 }),
                                    new MediaMimeType("image/png")))
                        ]));
            }
            """);
        File.WriteAllText(
            Path.Combine(output, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "tools": {
                "image": {
                  "catalogs": ["work"],
                  "pathArguments": ["path"]
                }
              }
            }
            """);

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        Assert.DoesNotContain(catalog.WorkTools, tool => tool.Definition.Name == "image");
        Assert.DoesNotContain(catalog.PlanMultimodalTools, tool => tool.Definition.Name == "image");
        var tool = Assert.Single(
            catalog.WorkMultimodalTools,
            tool => tool.Definition.Name == "image");
        var result = await tool.InvokeAsync(
            new MultimodalToolCall("1", "image", """{"path":"capture.png"}"""));

        var text = Assert.IsType<TextContent>(result.Contents[0]);
        using var arguments = JsonDocument.Parse(text.Text);
        Assert.Equal(
            Workspace.Canonicalize(Path.Combine(workspace.Path, "capture.png")),
            arguments.RootElement.GetProperty("path").GetString());
        Assert.IsType<ImageContent>(result.Contents[1]);
    }

    [Fact]
    public async Task Load_DotnetMultimodalTimeout_ReturnsFailure()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "SlowImageTools");
        PublishFixture(
            directory,
            """
            using System.Text.Json;
            using Crystal.Multimodal;
            using Crystal.Multimodal.Tools;
            using Crystal.Tools;

            namespace FixtureTools;

            public sealed class SlowImageTool : IMultimodalTool
            {
                public SlowImageTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("slow_image", document.RootElement.Clone(), "Slow image.");
                }

                public ToolDefinition Definition { get; }

                public async ValueTask<MultimodalToolOutput> InvokeAsync(
                    MultimodalToolCall call,
                    CancellationToken cancellationToken = default)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    return new MultimodalToolOutput([new TextContent("finished")]);
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "types": ["SlowImageTool"],
              "timeoutSeconds": 1
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);
        var tool = Assert.Single(catalog.WorkMultimodalTools);

        var result = await tool.InvokeAsync(new MultimodalToolCall("1", "slow_image", "{}"));

        Assert.Equal(MultimodalToolResultStatus.Failure, result.Status);
        var text = Assert.IsType<TextContent>(Assert.Single(result.Contents));
        Assert.Contains("timed out after 1 second", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_DotnetHostTool_UsesTheHostContractAssembly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, ".crystal", "tools", "HostTools");
        PublishFixture(
            output,
            """
            using System.Text.Json;

            using Crystal.Multimodal;
            using Crystal.Multimodal.Tools;
            using Crystal.Tools;

            using CrystalCode.Tools;

            namespace FixtureTools;

            public sealed class HostTool : IHostTool
            {
                public HostTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("where", document.RootElement.Clone(), "Where.");
                }

                public ToolDefinition Definition { get; }

                public ValueTask<ToolOutput> InvokeAsync(
                    ToolCall call,
                    ToolHostContext context,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(new ToolOutput(
                        context.WorkspaceRoot + "\n" + context.SessionId + "\n" + context.Approval));
            }

            public sealed class HostImageTool : IHostMultimodalTool
            {
                public HostImageTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("where_image", document.RootElement.Clone(), "Where image.");
                }

                public ToolDefinition Definition { get; }

                public ValueTask<MultimodalToolOutput> InvokeAsync(
                    MultimodalToolCall call,
                    ToolHostContext context,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(new MultimodalToolOutput(
                    [
                        new TextContent(
                            context.WorkspaceRoot + "\n" + context.SessionId + "\n" + context.Approval)
                    ]));
            }
            """,
            referenceHostContract: true);
        File.WriteAllText(
            Path.Combine(output, ExternalFiles.FileName),
            """
            {
              "runner": "dotnet",
              "assembly": "FixtureTools.dll",
              "types": ["HostTool", "HostImageTool"]
            }
            """);
        var root = new Workspace(workspace.Path);
        var catalog = ExternalCatalog.Load(
            home.Home,
            root,
            enabled: true,
            host: new SessionToolHost(root, () => "sess-9", () => "audit"));

        Assert.Empty(catalog.Notes);
        Assert.True(File.Exists(Path.Combine(output, "CrystalCode.Tools.dll")));
        var expected = root.Root + "\nsess-9\naudit";
        var text = await Assert.Single(catalog.WorkTools).InvokeAsync(new ToolCall("1", "where", "{}"));
        Assert.Equal(expected, text.Text);
        var pictured = await Assert.Single(catalog.WorkMultimodalTools)
            .InvokeAsync(new MultimodalToolCall("2", "where_image", "{}"));
        var content = Assert.IsType<TextContent>(Assert.Single(pictured.Contents));
        Assert.Equal(expected, content.Text);
    }

    private static void WriteExecSet(string workspace, string directoryName, string toolName)
    {
        var directory = Path.Combine(workspace, ".crystal", "tools", directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "command": ["/bin/true"],
              "tools": [
                {
                  "name": "{{toolName}}",
                  "description": "Exec stand-in.",
                  "schema": { "type": "object", "properties": {} }
                }
              ]
            }
            """);
    }

    private static void PublishFixture(
        string outputDirectory,
        string? extraTypeSource = null,
        bool referenceHostContract = false)
    {
        Directory.CreateDirectory(outputDirectory);
        using var source = new TemporaryWorkspace();
        var project = Path.Combine(source.Path, "src");
        Directory.CreateDirectory(project);
        var crystalDll = Path.Combine(AppContext.BaseDirectory, "Crystal.dll");
        var crystalToolsDll = Path.Combine(AppContext.BaseDirectory, "Crystal.Tools.dll");
        Assert.True(File.Exists(crystalDll), crystalDll);
        Assert.True(File.Exists(crystalToolsDll), crystalToolsDll);
        var hostReference = string.Empty;
        if (referenceHostContract)
        {
            var hostDll = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Tools.dll");
            Assert.True(File.Exists(hostDll), hostDll);
            hostReference = """
                <Reference Include="CrystalCode.Tools">
                  <HintPath>{hostDll}</HintPath>
                </Reference>
            """;
            hostReference = hostReference.Replace("{hostDll}", hostDll, StringComparison.Ordinal);
        }

        File.WriteAllText(
            Path.Combine(project, "FixtureTools.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>{Path.Combine(source.Path, "packages")}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Crystal">
                  <HintPath>{crystalDll}</HintPath>
                </Reference>
                <Reference Include="Crystal.Tools">
                  <HintPath>{crystalToolsDll}</HintPath>
                </Reference>
            {hostReference}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(project, "AlphaTool.cs"),
            """
            using Crystal.Tools;
            using System.Text.Json;

            namespace FixtureTools;

            public sealed class AlphaTool : ITool
            {
                public AlphaTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("alpha", document.RootElement.Clone(), "Alpha.");
                }

                public ToolDefinition Definition { get; }

                public ValueTask<ToolOutput> InvokeAsync(
                    ToolCall call,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(new ToolOutput("alpha-ok"));
            }
            """);
        File.WriteAllText(
            Path.Combine(project, "BetaTool.cs"),
            """
            using Crystal.Tools;
            using System.Text.Json;

            namespace FixtureTools;

            public sealed class BetaTool : ITool
            {
                public BetaTool()
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    Definition = new ToolDefinition("beta", document.RootElement.Clone(), "Beta.");
                }

                public ToolDefinition Definition { get; }

                public ValueTask<ToolOutput> InvokeAsync(
                    ToolCall call,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(new ToolOutput("beta-ok"));
            }
            """);
        File.WriteAllText(
            Path.Combine(project, "Marker.cs"),
            """
            namespace FixtureTools.Private;

            public static class Marker
            {
                public static string Value => "private";
            }
            """);

        if (extraTypeSource is not null)
        {
            File.WriteAllText(Path.Combine(project, "ExtraTool.cs"), extraTypeSource);
        }

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
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(1)));
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stdout + stderr);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "FixtureTools.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "Crystal.Tools.dll")));
    }
}
