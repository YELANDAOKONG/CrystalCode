using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

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

    [Fact]
    public void Collect_SkipsProjectRulesWhenThatSideIsOff()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(home.Home.Root, InstructionNames.Agents), "global agents");
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "project agents");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var text = string.Join(
            "\n",
            discovery.Collect(workspace.Path, new RuleFileSettings(globalEnabled: true, projectEnabled: false)));

        Assert.Contains("global agents", text, StringComparison.Ordinal);
        Assert.DoesNotContain("project agents", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_SkipsGlobalRulesWhenThatSideIsOff()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(home.Home.Root, InstructionNames.Agents), "global agents");
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "project agents");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var text = string.Join(
            "\n",
            discovery.Collect(workspace.Path, new RuleFileSettings(globalEnabled: false, projectEnabled: true)));

        Assert.DoesNotContain("global agents", text, StringComparison.Ordinal);
        Assert.Contains("project agents", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_AppendsOutermostFileFirstSoTheClosestFileWins()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "root rules");
        File.WriteAllText(Path.Combine(nested, InstructionNames.Agents), "nested rules");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var text = string.Join("\n\n", discovery.Collect(nested));

        var rootIndex = text.IndexOf("root rules", StringComparison.Ordinal);
        var nestedIndex = text.IndexOf("nested rules", StringComparison.Ordinal);
        Assert.True(rootIndex >= 0, "The root rule file was not collected.");
        Assert.True(
            nestedIndex > rootIndex,
            "The closest rule file must come last so it is the most specific.");
    }

    [Fact]
    public void Collect_TakesOneFilePerDirectory()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "root agents");
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Claude), "root claude");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var text = string.Join("\n\n", discovery.Collect(workspace.Path));

        Assert.Contains("root agents", text, StringComparison.Ordinal);
        Assert.DoesNotContain("root claude", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_UsesNestedClaudeWhenThatDirectoryHasNoAgents()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var nested = Path.Combine(workspace.Path, "services");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "root agents");
        File.WriteAllText(Path.Combine(nested, InstructionNames.Claude), "nested claude");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var text = string.Join("\n\n", discovery.Collect(nested));

        Assert.Contains("root agents", text, StringComparison.Ordinal);
        Assert.Contains("nested claude", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_StopsAtTheCharacterBudget()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        var line = new string('x', 99) + "\n";
        var content = string.Concat(Enumerable.Repeat(line, InstructionDiscovery.MaxInstructionCharacters / 100 + 100));
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), content);
        var discovery = InstructionDiscovery.Isolated(home.Home);

        var parts = discovery.Collect(workspace.Path);
        var text = string.Join("\n\n", parts);

        Assert.Contains(InstructionDiscovery.TruncationNote, text, StringComparison.Ordinal);
        Assert.True(
            text.Length < InstructionDiscovery.MaxInstructionCharacters + 512,
            "Rule files must stay inside the character budget.");
    }

    [Fact]
    public void Collect_SkipsUnreadableAgentsAndUsesClaude()
    {
        // Root ignores file modes, so the file cannot be made unreadable.
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var agents = Path.Combine(workspace.Path, InstructionNames.Agents);
        File.WriteAllText(agents, "secret agents");
        File.SetUnixFileMode(agents, UnixFileMode.None);
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Claude), "claude fallback");
        var discovery = InstructionDiscovery.Isolated(home.Home);

        try
        {
            var parts = discovery.Collect(workspace.Path);
            var text = string.Join("\n\n", parts);

            Assert.Contains("claude fallback", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret agents", text, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(agents, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
