using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class TodoBarCommandTests
{
    [Fact]
    public async Task Bar_TogglesAndPublishesThePreference()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/todos bar", CancellationToken.None);
        var off = headless.Observer.Events.OfType<PreferencesChanged>().Last().Preferences;
        Assert.False(off.TodoBar);
        Assert.Equal("false", Read(headless, "todoBar"));

        await headless.Session.SubmitAsync("/todos bar on", CancellationToken.None);
        var on = headless.Observer.Events.OfType<PreferencesChanged>().Last().Preferences;
        Assert.True(on.TodoBar);
        Assert.Null(Read(headless, "todoBar"));
    }

    [Fact]
    public async Task Rows_AcceptsTheSupportedRangeAndRejectsTheRest()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/todos rows 10", CancellationToken.None);
        Assert.Equal(10, headless.Observer.Events.OfType<PreferencesChanged>().Last().Preferences.TodoBarRows);
        Assert.Equal("10", Read(headless, "todoBarRows"));

        foreach (var bad in new[] { "2", "11", "x", "-3" })
        {
            await headless.Session.SubmitAsync("/todos rows " + bad, CancellationToken.None);
        }

        Assert.Equal(4, headless.Observer.Events.OfType<ErrorWritten>().Count());
        Assert.Equal(10, headless.Observer.Events.OfType<PreferencesChanged>().Last().Preferences.TodoBarRows);

        await headless.Session.SubmitAsync("/todos rows 4", CancellationToken.None);
        Assert.Null(Read(headless, "todoBarRows"));
    }

    [Fact]
    public void Settings_RejectRowsOutsideTheRange()
    {
        var settings = HarnessSettings.CreateDefault();

        Assert.True(settings.TodoBar);
        Assert.Equal(4, settings.TodoBarRows);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithTodoBarRows(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithTodoBarRows(11));
    }

    private static string? Read(HeadlessSession headless, string name)
    {
        if (!File.Exists(headless.Home.ConfigPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(headless.Home.ConfigPath));
        return document.RootElement.TryGetProperty(name, out var value) ? value.ToString().ToLowerInvariant() : null;
    }
}
