using System.Text.Json;

using Crystal.Chat;
using CrystalCode.Providers.Protocol;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Providers.Tests.Responses;

public sealed class ResponsesInstructionsTests
{
    private static readonly ProtocolOptions Options = new(
        string.Empty, "m", new Uri("https://example.test/v1/"), null, null, null, TimeSpan.FromMinutes(1), "x");

    private static ChatRequest Request() => new(
    [
        new ChatMessage(ChatRole.System, "first rule"),
        new ChatMessage(ChatRole.User, "hello"),
        new ChatMessage(ChatRole.System, "second rule")
    ]);

    [Fact]
    public void SystemAsInstructions_MovesSystemTextOutOfInput()
    {
        var body = new ResponsesCodec("x", systemAsInstructions: true).WriteRequest(Options, Request(), stream: true);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("first rule\n\nsecond rule", root.GetProperty("instructions").GetString());
        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Single(input);
        Assert.Equal("user", input[0].GetProperty("role").GetString());
    }

    [Fact]
    public void Default_KeepsSystemMessagesInInput()
    {
        var body = new ResponsesCodec("x").WriteRequest(Options, Request(), stream: true);

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.TryGetProperty("instructions", out _));
        Assert.Equal(3, document.RootElement.GetProperty("input").GetArrayLength());
    }
}
