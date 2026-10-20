using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class AgentCatalogTests
{
    [Fact]
    public void Catalog_WorkspaceEntryReplacesEntireHomeDefinitionIncludingDisable()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteAgent(home.Root, "reviewer", """{"enabled":true,"description":"Home reviewer","provider":"home","model":"home"}""", "Home prompt");
        WriteAgent(Path.Combine(workspace.Path, ".crystal"), "reviewer", """{"enabled":true,"description":"Project reviewer","tools":["read"]}""", "Project prompt");
        var catalog = AgentCatalog.Load(home.Home, workspace.Path, new AgentSettings { Enabled = true });
        var reviewer = Assert.Single(catalog.Definitions, agent => agent.Name == "reviewer");
        Assert.Equal("Project prompt", reviewer.Prompt);
        Assert.Null(reviewer.Model.Provider);
        Assert.Equal(["read"], reviewer.Tools);
        WriteAgent(Path.Combine(workspace.Path, ".crystal"), "reviewer", """{"description":"Disabled"}""", "unused");
        catalog = AgentCatalog.Load(home.Home, workspace.Path, new AgentSettings { Enabled = true });
        Assert.DoesNotContain(catalog.Definitions, agent => agent.Name == "reviewer");
    }

    [Fact]
    public void Catalog_ReservedNamesAreSkippedWithDiagnosticAndMissingDescriptionIsRejected()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteAgent(home.Root, "explore", """{"enabled":true,"description":"override"}""", "bad");
        WriteAgent(home.Root, "broken", """{"enabled":true}""", "bad");
        var catalog = AgentCatalog.Load(home.Home, workspace.Path, new AgentSettings { Enabled = true });
        Assert.Equal(3, catalog.Definitions.Count);
        Assert.Contains(catalog.Notes, note => note.Contains("reserved", StringComparison.Ordinal));
        Assert.Contains(catalog.Notes, note => note.Contains("description", StringComparison.Ordinal));
        Assert.Empty(AgentCatalog.Load(home.Home, workspace.Path, new AgentSettings()).Definitions);
    }

    [Fact]
    public void AgentSettings_RoundTripAndCopiesPreserveAllAgentOptions()
    {
        using var home = new TemporaryHome();
        var agents = new AgentSettings { Enabled = true, MaxConcurrent = 3,
            Explore = new AgentModelSettings { Provider = "local", Model = "text", Thinking = "high", Context = 100_000, Tier = "standard", Speed = "fast" } };
        var settings = HarnessSettings.CreateDefault().WithAgents(agents);
        var store = new DiskSettingsStore(home.Home);
        store.Save(settings);
        Assert.Equal(agents, store.Load().Agents);
        Assert.Equal(agents, settings.WithThinkingEffort(ThinkingSelection.Off).Agents);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentSettings { MaxConcurrent = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentSettings { MaxConcurrent = 9 });
    }

    private static void WriteAgent(string root, string name, string json, string prompt)
    {
        var directory = Path.Combine(root, "agents", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "agent.json"), json);
        File.WriteAllText(Path.Combine(directory, "prompt.md"), prompt);
    }
}
