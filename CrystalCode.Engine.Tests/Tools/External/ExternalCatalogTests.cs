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
