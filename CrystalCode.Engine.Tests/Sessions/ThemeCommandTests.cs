using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ThemeCommandTests
{
    // Reading the saved settings back needs the provider file the session was built from.
    private static readonly HarnessSettings Settings = HeadlessSession.ScriptedSettings();

    private static Action<CrystalHome, string> WriteProviders(HarnessSettings settings) => (home, _) =>
    {
        home.EnsureCreated();
        File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
    };

    [Fact]
    public async Task Theme_WithoutAnArgumentOffersTheNamesAndSavesTheChoice()
    {
        IReadOnlyList<string>? offered = null;
        string? offeredCurrent = null;
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings, prepare: WriteProviders(Settings), chooseTheme: (names, current, _) =>
        {
            offered = names;
            offeredCurrent = current;
            return Task.FromResult<string?>("light");
        });
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/theme", CancellationToken.None);

        Assert.Equal(["default", "dark", "light"], offered);
        Assert.Equal("default", offeredCurrent);
        Assert.Equal("light", new DiskSettingsStore(session.Home).Load().Theme);
        Assert.Contains(session.Observer.Events.OfType<NoteWritten>(), note => note.Text == "Theme  light");
    }

    [Fact]
    public async Task Theme_CancelledPickerChangesNothing()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings, prepare: WriteProviders(Settings), chooseTheme: (_, _, _) =>
            Task.FromResult<string?>(null));
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/theme", CancellationToken.None);

        Assert.False(File.Exists(session.Home.ConfigPath));
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
        Assert.DoesNotContain(session.Observer.Events.OfType<NoteWritten>(), note => note.Text.StartsWith("Theme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Theme_WithoutAChooserListsTheNamesAsANote()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings, prepare: WriteProviders(Settings));
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/theme", CancellationToken.None);

        Assert.Contains(session.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.StartsWith("Theme  default", StringComparison.Ordinal)
                && note.Text.Contains("Available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Theme_WithANameStillSkipsThePicker()
    {
        var asked = false;
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings, prepare: WriteProviders(Settings), chooseTheme: (_, _, _) =>
        {
            asked = true;
            return Task.FromResult<string?>(null);
        });
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/theme dark", CancellationToken.None);

        Assert.False(asked);
        Assert.Equal("dark", new DiskSettingsStore(session.Home).Load().Theme);
    }
}
