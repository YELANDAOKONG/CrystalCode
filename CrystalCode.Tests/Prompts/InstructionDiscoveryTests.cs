using CrystalCode.Prompts;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class InstructionDiscoveryTests
{
    [Fact]
    public void Collect_CombinesGlobalAndProjectAgents()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(home.Home.Root, InstructionNames.Agents), "global agents");
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "project agents");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var parts = discovery.Collect(workspace.Path);
        var text = string.Join("\n\n", parts);

        Assert.Contains("global agents", text, StringComparison.Ordinal);
        Assert.Contains("project agents", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_GlobalAgentsWinsOverClaudeFallback()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(home.Home.Root, InstructionNames.Agents), "crystal agents");
        File.WriteAllText(Path.Combine(home.Home.Root, InstructionNames.Claude), "crystal claude");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var parts = discovery.Collect(workspace.Path);
        var text = string.Join("\n\n", parts);

        Assert.Contains("crystal agents", text, StringComparison.Ordinal);
        Assert.DoesNotContain("crystal claude", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_UsesOpenCodeGlobalWhenCrystalAgentsIsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var discovery = InstructionDiscovery.Isolated(home.Home);
        var opencode = Path.Combine(home.Home.Root, "xdg-config", "opencode");
        Directory.CreateDirectory(opencode);
        File.WriteAllText(Path.Combine(opencode, InstructionNames.Agents), "opencode global");

        var parts = discovery.Collect(workspace.Path);
        var text = string.Join("\n\n", parts);

        Assert.Contains("opencode global", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_UsesClaudeGlobalWhenNoAgentsFileExists()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var discovery = InstructionDiscovery.Isolated(home.Home);
        var claude = Path.Combine(home.Home.Root, "profile", ".claude");
        Directory.CreateDirectory(claude);
        File.WriteAllText(Path.Combine(claude, InstructionNames.Claude), "claude global");

        var parts = discovery.Collect(workspace.Path);
        var text = string.Join("\n\n", parts);

        Assert.Contains("claude global", text, StringComparison.Ordinal);
    }
}
