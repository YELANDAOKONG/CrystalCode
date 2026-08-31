using System.Diagnostics;

using Crystal.Tools;

using CrystalCode.Home;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;
using CrystalCode.Tools.External;

using Xunit;

namespace CrystalCode.Tests.Tools.External;

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
        WriteExecSet(workspace.Path, "alpha");

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

    private static void WriteExecSet(string workspace, string name)
    {
        var directory = Path.Combine(workspace, ".crystal", "tools", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "description": "Exec stand-in.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);
    }

    private static void PublishFixture(string outputDirectory, string? extraTypeSource = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var project = Path.Combine(outputDirectory, "src");
        Directory.CreateDirectory(project);
        var crystalDll = Path.Combine(AppContext.BaseDirectory, "Crystal.dll");
        var crystalToolsDll = Path.Combine(AppContext.BaseDirectory, "Crystal.Tools.dll");
        Assert.True(File.Exists(crystalDll), crystalDll);
        Assert.True(File.Exists(crystalToolsDll), crystalToolsDll);
        File.WriteAllText(
            Path.Combine(project, "FixtureTools.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>{Path.Combine(outputDirectory, "packages")}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Crystal">
                  <HintPath>{crystalDll}</HintPath>
                </Reference>
                <Reference Include="Crystal.Tools">
                  <HintPath>{crystalToolsDll}</HintPath>
                </Reference>
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
        Assert.True(File.Exists(Path.Combine(outputDirectory, "Crystal.Tools.dll")));
    }
}
