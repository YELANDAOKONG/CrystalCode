using System.Text.Json;

using Crystal.Chat;
using Crystal.Multimodal.Chat;
using Crystal.Reasoning;
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

    [Fact]
    public void StreamedAssistantPhase_IsReplayedOnItsMessageEvenAfterTheNextItemStarts()
    {
        var codec = new ResponsesCodec("x");
        var parser = codec.CreateStreamParser();
        using var delta = JsonDocument.Parse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"Checking."}""");
        using var next = JsonDocument.Parse(
            """{"type":"response.output_text.delta","output_index":1,"delta":"Done."}""");
        using var done = JsonDocument.Parse(
            """{"type":"response.output_item.done","output_index":0,"item":{"type":"message","role":"assistant","phase":"commentary","content":[{"type":"output_text","text":"Checking."}]}}""");

        var first = Assert.IsType<ChatTextDelta>(Assert.Single(parser.Parse(delta.RootElement)));
        var second = Assert.IsType<ChatTextDelta>(Assert.Single(parser.Parse(next.RootElement)));
        var phase = Assert.IsType<ChatReasoningStateReceived>(Assert.Single(parser.Parse(done.RootElement)));
        Assert.InRange(phase.ItemIndex, first.ItemIndex + 1, second.ItemIndex - 1);

        var request = new ChatRequest(
        [
            new ChatMessage(first.Role, first.Text),
            new ChatReasoningItem(new ReasoningContent([], phase.State)),
            new ChatMessage(second.Role, second.Text)
        ]);
        using var body = JsonDocument.Parse(codec.WriteRequest(Options, request, stream: true));
        var input = body.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(2, input.Length);
        Assert.Equal("commentary", input[0].GetProperty("phase").GetString());
        Assert.False(input[1].TryGetProperty("phase", out _));
    }

    [Fact]
    public void MultimodalAssistantPhase_RoundTripsThroughTheCompletedResponse()
    {
        var codec = new ResponsesMultimodalCodec("x", shape: new ResponsesRequestShape(CompleteOverStream: true));
        using var completed = JsonDocument.Parse(
            """
            {"type":"response.completed","response":{"status":"completed","output":[
              {"type":"message","role":"assistant","phase":"final_answer","content":[{"type":"output_text","text":"Done."}]}
            ]}}
            """);
        var response = codec.StreamCompletionReader!(completed.RootElement)!;
        var request = new MultimodalChatRequest(Assert.Single(response.Candidates).Items);
        using var body = JsonDocument.Parse(codec.WriteRequest(Options, request, stream: true));
        var message = Assert.Single(body.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("final_answer", message.GetProperty("phase").GetString());
        Assert.Equal("Done.", message.GetProperty("content")[0].GetProperty("text").GetString());
    }
}
