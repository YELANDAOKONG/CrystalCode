using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class ExternalCatalogTests
{
    [Fact]
    public async Task Load_ExecStdinJson_RunsCommand()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        var script = WriteStdinScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo stdin.",
              "schema": { "type": "object", "properties": { "tag": { "type": "string" } } },
              "command": ["{{script.Replace("\\", "/")}}"]
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        var tool = Assert.Single(catalog.WorkTools);
        Assert.Contains(catalog.PlanTools, item => item.Definition.Name == "echojson");
        var output = await tool.InvokeAsync(
            new ToolCall("1", "echojson", """{"tag":"ok"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        Assert.Contains("ok", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ExecStdinJson_DoesNotWriteUtf8Bom()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        var script = WriteStdinProbeScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Capture stdin bytes.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"]
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "echojson", """{"tag":"ok"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        var probe = Path.Combine(workspace.Path, "stdin.bin");
        Assert.True(File.Exists(probe), output.Text);
        var bytes = File.ReadAllBytes(probe);
        Assert.NotEmpty(bytes);
        Assert.NotEqual((byte)0xEF, bytes[0]);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public async Task Load_ExecLargeStdinAndOutput_DrainsAndTruncates()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        var script = WriteStdinScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo stdin.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"],
              "timeoutSeconds": 5
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);
        var arguments = "{\"payload\":\"" + new string('x', WorkspaceLimits.MaximumToolOutputCharacters + 10_000) + "\"}";

        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "echojson", arguments));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("[truncated to", output.Text, StringComparison.Ordinal);
        Assert.True(output.Text.Length < WorkspaceLimits.MaximumToolOutputCharacters + 100);
    }

    [Fact]
    public async Task Load_ExecCancellation_StopsChildProcess()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "sleeping");
        Directory.CreateDirectory(directory);
        var script = WriteDelayedMarkerScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Wait then write a marker.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"],
              "stdin": false,
              "timeoutSeconds": "unlimited"
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);
        using var cancellation = new CancellationTokenSource();
        var invocation = catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "sleeping", "{}"),
            cancellation.Token).AsTask();
        var started = Path.Combine(workspace.Path, "started.txt");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(started), TimeSpan.FromSeconds(5)));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.False(File.Exists(Path.Combine(workspace.Path, "survived.txt")));
    }

    [Fact]
    public async Task Load_ExecArgv_AppendsFlags()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echoargv");
        Directory.CreateDirectory(directory);
        var script = WriteArgvScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo argv.",
              "schema": { "type": "object", "properties": { "environment": { "type": "string" } } },
              "command": ["{{script.Replace("\\", "/")}}"],
              "stdin": false,
              "argv": { "environment": "--env" }
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "echoargv", """{"environment":"prod"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        Assert.Contains("prod", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Disabled_IsEmpty()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteManifest(workspace.Path, "echojson", """["/bin/true"]""");
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: false);

        Assert.Empty(catalog.WorkTools);
        Assert.Empty(catalog.PlanTools);
    }

    [Fact]
    public void Load_AddsExternalToolToWorkspaceCatalog()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"],
              "catalogs": ["work"]
            }
            """);
        var external = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var plan = WorkspaceCatalog.CreatePlan(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            external: external);
        var work = WorkspaceCatalog.CreateWork(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            external: external);

        Assert.Null(plan.Find("echojson"));
        Assert.NotNull(work.Find("echojson"));
    }

    [Fact]
    public void Load_HomeAuthorPolicy_HonorsAlwaysApproval()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.ToolsDirectory, "echojson");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "approval": "always",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true,
            approvalSettings: ExternalToolApprovalSettings.Default);

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal(ExternalToolSource.Home, tool.Source);
        Assert.Equal(ExternalApprovalMode.Always, tool.EffectiveApproval);
        Assert.Contains("echojson", catalog.AutomaticTools);
    }

    [Fact]
    public void Load_ProjectHostPolicy_IgnoresAlwaysApproval()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "approval": "always",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);

        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true,
            approvalSettings: ExternalToolApprovalSettings.Default);

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal(ExternalToolSource.Project, tool.Source);
        Assert.Equal(ExternalApprovalMode.Always, tool.DeclaredApproval);
        Assert.Equal(ExternalApprovalMode.Inherit, tool.EffectiveApproval);
        Assert.Empty(catalog.AutomaticTools);
    }

    [Fact]
    public async Task Load_ExecHostEnvironment_ReachesTheChild()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "showenv");
        Directory.CreateDirectory(directory);
        var script = WriteEnvScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Print host facts.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"],
              "stdin": false
            }
            """);
        var root = new Workspace(workspace.Path);
        var catalog = ExternalCatalog.Load(
            home.Home,
            root,
            enabled: true,
            host: new SessionToolHost(root, () => "sess-9", () => "audit"));

        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "showenv", "{}"));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        Assert.Contains(root.Root, output.Text, StringComparison.Ordinal);
        Assert.Contains("sess-9", output.Text, StringComparison.Ordinal);
        Assert.Contains("audit", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ExecContent_ReturnsTextAndImage()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        File.WriteAllBytes(Path.Combine(workspace.Path, "chart.png"), png);
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "render");
        Directory.CreateDirectory(directory);
        var payload = Path.Combine(directory, "payload.json");
        File.WriteAllText(
            payload,
            """{"text":"rendered","images":[{"mimeType":"image/png","path":"chart.png"}]}""");
        var script = WriteFileScript(directory, payload);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Render a chart.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"],
              "stdin": false,
              "output": "content"
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var text = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "render", "{}"));
        Assert.Equal(ToolResultStatus.Failure, text.Status);
        Assert.Contains("cannot accept tool images", text.Text, StringComparison.Ordinal);

        var imageTool = Assert.Single(catalog.WorkMultimodalTools);
        var image = await imageTool.InvokeAsync(new MultimodalToolCall("2", "render", "{}"));
        Assert.Equal(MultimodalToolResultStatus.Success, image.Status);
        Assert.Equal("rendered", Assert.IsType<TextContent>(image.Contents[0]).Text);
        Assert.Equal("image/png", Assert.IsType<ImageContent>(image.Contents[1]).Image.MimeType.Value);
    }

    private static void WriteManifest(string workspace, string name, string commandJson)
    {
        var directory = Path.Combine(workspace, ".crystal", "tools", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": {{commandJson}}
            }
            """);
    }

    private static string WriteStdinScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "echo-stdin.cmd");
            File.WriteAllText(script, "@echo off\r\nmore\r\n");
            return script;
        }

        var path = Path.Combine(directory, "echo-stdin.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteArgvScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "echo-argv.cmd");
            File.WriteAllText(script, "@echo off\r\necho %*\r\n");
            return script;
        }

        var path = Path.Combine(directory, "echo-argv.sh");
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' \"$@\"\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteStdinProbeScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "probe-stdin.cmd");
            File.WriteAllText(
                script,
                """
                @echo off
                powershell -NoProfile -Command "$s=[Console]::OpenStandardInput(); $f=[IO.File]::Create('stdin.bin'); $s.CopyTo($f); $f.Close()"
                """);
            return script;
        }

        var path = Path.Combine(directory, "probe-stdin.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat > stdin.bin\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteEnvScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "show-env.cmd");
            File.WriteAllText(
                script,
                "@echo off\r\necho %CRYSTAL_WORKSPACE%\r\necho %CRYSTAL_SESSION%\r\necho %CRYSTAL_APPROVAL%\r\n");
            return script;
        }

        var path = Path.Combine(directory, "show-env.sh");
        File.WriteAllText(
            path,
            "#!/bin/sh\nprintf '%s\\n' \"$CRYSTAL_WORKSPACE\" \"$CRYSTAL_SESSION\" \"$CRYSTAL_APPROVAL\"\n");
        File.SetUnixFileMode(path, ExecutableMode());
        return path;
    }

    private static string WriteFileScript(string directory, string payloadPath)
    {
        var quoted = payloadPath.Replace("\"", "\\\"");
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "print-file.cmd");
            File.WriteAllText(script, "@echo off\r\ntype \"" + quoted + "\"\r\n");
            return script;
        }

        var path = Path.Combine(directory, "print-file.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat \"" + quoted + "\"\n");
        File.SetUnixFileMode(path, ExecutableMode());
        return path;
    }

    private static UnixFileMode ExecutableMode() =>
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherExecute;

    private static string WriteDelayedMarkerScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "delayed-marker.cmd");
            File.WriteAllText(
                script,
                "@echo off\r\necho ready>started.txt\r\nping -n 3 127.0.0.1 >nul\r\necho alive>survived.txt\r\n");
            return script;
        }

        var path = Path.Combine(directory, "delayed-marker.sh");
        File.WriteAllText(
            path,
            "#!/bin/sh\nprintf ready > started.txt\nsleep 2\nprintf alive > survived.txt\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }
}
