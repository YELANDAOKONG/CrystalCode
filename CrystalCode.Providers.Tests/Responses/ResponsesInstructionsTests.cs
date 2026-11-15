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
        var body = new ResponsesCodec("x", shape: new ResponsesRequestShape(SystemAsInstructions: true)).WriteRequest(Options, Request(), stream: true);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("first rule\n\nsecond rule", root.GetProperty("instructions").GetString());
        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Single(input);
        Assert.Equal("user", input[0].GetProperty("role").GetString());
    }

    [Fact]
    public void OmitSamplingAndNoReasoningInclude_LeaveThoseFieldsOut()
    {
        var options = Options with { Temperature = 0.2, TopP = 0.9, MaxTokens = 100 };
        var shape = new ResponsesRequestShape(IncludeEncryptedReasoning: false, OmitSampling: true);

        using var shaped = JsonDocument.Parse(
            new ResponsesCodec("x", shape: shape).WriteRequest(options, Request(), stream: true));
        using var plain = JsonDocument.Parse(
            new ResponsesCodec("x").WriteRequest(options, Request(), stream: true));

        foreach (var name in new[] { "temperature", "top_p", "max_output_tokens", "include" })
        {
            Assert.False(shaped.RootElement.TryGetProperty(name, out _), name);
            Assert.True(plain.RootElement.TryGetProperty(name, out _), name);
        }
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
