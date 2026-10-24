using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class ProviderFilesTests
{
    [Fact]
    public void Load_ReadsRootAndCategoryFilesTogether()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, Provider("main", "https://main.example.test/", "m"));
        Write(root, "extra.json", Provider("extra", "https://extra.example.test/", "e"));
        Write(root, "work/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));
        Write(root, "work/local.json", Provider("local", "http://localhost:9000/", "l"));
        Write(root, "home/other.json", Provider("other", "https://other.example.test/", "o"));

        var catalog = store.Load().Catalog;

        Assert.Equal(1000, catalog.GetModel(new ProviderName("main"), "m").ContextWindow);
        Assert.Equal(1000, catalog.GetModel(new ProviderName("extra"), "e").ContextWindow);
        Assert.Equal(1000, catalog.GetModel(new ProviderName("gateway"), "g").ContextWindow);
        Assert.Equal(1000, catalog.GetModel(new ProviderName("local"), "l").ContextWindow);
        Assert.Equal(1000, catalog.GetModel(new ProviderName("other"), "o").ContextWindow);
    }

    [Fact]
    public void Load_ReadsProvidersDirectoryWithoutProvidersFile()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "cloud/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        var catalog = store.Load().Catalog;

        Assert.Equal(1000, catalog.GetModel(new ProviderName("gateway"), "g").ContextWindow);
        Assert.Contains("deepseek", catalog.Providers.Keys);
    }

    [Fact]
    public void Load_IgnoresLegacyConfigProvidersWhenOnlyDirectoryExists()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        root.Home.EnsureCreated();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "providers": {
                "legacy": {
                  "protocol": "openai",
                  "baseUri": "https://legacy.example.test/",
                  "models": { "old": { "contextWindow": 1000 } }
                }
              }
            }
            """);
        Write(root, "cloud/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        var catalog = store.Load().Catalog;

        Assert.True(catalog.Providers.ContainsKey("gateway"));
        Assert.False(catalog.Providers.ContainsKey("legacy"));
    }

    [Fact]
    public void Load_ReadsJsoncCategoryFile()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(
            root,
            "cloud/gateway.json",
            """
            {
              // A commented file in a category.
              "gateway": {
                "protocol": "openai",
                "baseUri": "https://gateway.example.test/",
                "models": { "g": { "contextWindow": 2048, }, },
              },
            }
            """);

        Assert.Equal(
            2048,
            store.Load().Catalog.GetModel(new ProviderName("gateway"), "g").ContextWindow);
    }

    [Fact]
    public void Load_RejectsProviderNameDefinedInTwoFiles()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, Provider("gateway", "https://one.example.test/", "one"));
        Write(root, "cloud/gateway.json", Provider("gateway", "https://two.example.test/", "two"));

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("'gateway'", error.Message, StringComparison.Ordinal);
        Assert.Contains("providers.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("gateway.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsProviderNameDefinedInTwoCategories()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "a/one.json", Provider("gateway", "https://one.example.test/", "one"));
        Write(root, "b/two.json", Provider("gateway", "https://two.example.test/", "two"));

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("one.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("two.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NamesTheFileThatIsInvalid()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "cloud/broken.json", """{ "gateway": { "protocol": "openai" } }""");

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("broken.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("baseUri", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NamesTheFileWithMalformedJson()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "cloud/broken.json", "{ not json");

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("broken.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1work")]
    [InlineData("has space")]
    [InlineData("_hidden")]
    public void Load_RejectsInvalidCategoryDirectoryName(string category)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, $"{category}/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains(category, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsCategoryNameLongerThanSixtyFourCharacters()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, $"{new string('a', 65)}/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        Assert.Throws<InvalidOperationException>(() => store.Load());
    }

    [Fact]
    public void Load_AcceptsCategoryNameThatMatchesToolSetRule()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "Work.Team_1-a/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        Assert.True(store.Load().Catalog.Providers.ContainsKey("gateway"));
    }

    [Fact]
    public void Load_DoesNotReadBelowTheCategory()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "cloud/nested/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));
        Write(root, "cloud/notes.txt", "not a provider file");

        var catalog = store.Load().Catalog;

        Assert.False(catalog.Providers.ContainsKey("gateway"));
    }

    [Fact]
    public void Load_CategoryDoesNotChangeProviderIdentity()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Write(root, "cloud/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));

        var settings = store.Load();

        Assert.False(settings.Catalog.Providers.ContainsKey("cloud"));
        Assert.Equal(
            "gateway",
            settings.Catalog.GetModelProvider(new ProviderName("gateway"), "g").Name.Value);
    }

    [Fact]
    public void Save_DoesNotWriteProvidersWhenDirectoryExists()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        root.Home.EnsureCreated();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "providers": {
                "legacy": {
                  "protocol": "openai",
                  "baseUri": "https://legacy.example.test/",
                  "models": { "old": { "contextWindow": 1000 } }
                }
              }
            }
            """);
        var file = Write(root, "cloud/gateway.json", Provider("gateway", "https://gateway.example.test/", "g"));
        var before = File.ReadAllText(file);

        store.Save(store.Load().WithPromptSet("concise"));

        Assert.False(File.Exists(root.Home.ProvidersPath));
        Assert.Equal(before, File.ReadAllText(file));
    }

    private static string Provider(string name, string baseUri, string model) =>
        $$"""
        {
          "{{name}}": {
            "protocol": "openai",
            "baseUri": "{{baseUri}}",
            "models": { "{{model}}": { "contextWindow": 1000 } }
          }
        }
        """;

    private static string Write(TemporaryHome root, string relativePath, string contents)
    {
        var path = Path.Combine(
            root.Home.ProvidersDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
}
