using System.Text.Json;

using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Events;

using Xunit;

namespace CrystalCode.Bridge.Tests;

public sealed class CrystalWirePreferencesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preferences_WritesTheThemeAndApprovalColors(bool approvalColors)
    {
        var preferences = new SessionPreferences(
            false, true, 4, true, true, true, true, true, true, true, false, [], [new("model", false, 100)], ["model"], "night", approvalColors);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            CrystalWire.Preferences(writer, "preferences", preferences);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        var written = document.RootElement.GetProperty("preferences");
        Assert.Equal("night", written.GetProperty("theme").GetString());
        Assert.Equal(approvalColors, written.GetProperty("approvalColors").GetBoolean());
        Assert.Equal(100, written.GetProperty("statusLineRules")[0].GetProperty("hideBelow").GetInt32());
        Assert.Equal("model", written.GetProperty("statusLineDrop")[0].GetString());
    }
}
