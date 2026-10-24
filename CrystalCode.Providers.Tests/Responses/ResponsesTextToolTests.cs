using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Providers.Tests.Responses;

public sealed class ResponsesTextToolTests
{
    private static readonly Uri BaseUri = new("https://example.test/v1/");

    private static readonly TextToolBinding Patch = new("apply_patch", "patchText");

    private static ToolDefinition PatchTool() =>
        ToolDefinition.Json(
            "apply_patch",
            JsonDocument.Parse(
                """{"type":"object","properties":{"patchText":{"type":"string"}},"required":["patchText"]}""")
                .RootElement,
            "Applies a patch");

    private static ToolDefinition ReadTool() =>
        ToolDefinition.Json(
            "read",
            JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement,
            "Reads a file");

    private static ResponsesProvider Provider(RecordingHandler handler, params TextToolBinding[] tools)
    {
        var http = new HttpClient(handler);
        return new ResponsesProvider(
            new ResponsesOptions("key", "gpt-test", BaseUri, textTools: tools),
            http);
    }

    [Fact]
    public async Task Request_DeclaresADeclaredToolAsACustomToolWithoutAParameterSchema()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler, Patch);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "go")],
            [PatchTool(), ReadTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var tools = sent.RootElement.GetProperty("tools");
        var patch = tools[0];
        Assert.Equal("custom", patch.GetProperty("type").GetString());
        Assert.Equal("apply_patch", patch.GetProperty("name").GetString());
        Assert.Equal("Applies a patch", patch.GetProperty("description").GetString());
        Assert.False(patch.TryGetProperty("parameters", out _));
        var read = tools[1];
        Assert.Equal("function", read.GetProperty("type").GetString());
        Assert.True(read.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task Request_KeepsEveryToolAFunctionWhenNothingIsDeclared()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler);

        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "go")], [PatchTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal("function", sent.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Request_KeepsAReplacedSchemaAFunctionTool()
    {
        var replaced = ToolDefinition.Json(
            "apply_patch",
            JsonDocument.Parse(
                """{"type":"object","properties":{"patchText":{"type":"string"},"dryRun":{"type":"boolean"}}}""")
                .RootElement);
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler, Patch);

        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "go")], [replaced]));

        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal("function", sent.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Request_ReplaysACallAndItsResultInCustomForm()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler, Patch);
        var patch = "*** Begin Patch\n*** Add File: a.txt\n+x\n*** End Patch";
        var arguments = JsonSerializer.Serialize(new { patchText = patch });

        await provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "go"),
                new ToolCall("call_1", "apply_patch", arguments, ToolInputKind.Json),
                new ToolResult("call_1", "Applied patch:\nA a.txt")
            ],
            [PatchTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var input = sent.RootElement.GetProperty("input");
        Assert.Equal("custom_tool_call", input[1].GetProperty("type").GetString());
        Assert.Equal("call_1", input[1].GetProperty("call_id").GetString());
        Assert.Equal(patch, input[1].GetProperty("input").GetString());
        Assert.Equal("custom_tool_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("call_1", input[2].GetProperty("call_id").GetString());
        Assert.Equal("Applied patch:\nA a.txt", input[2].GetProperty("output").GetString());
    }

    [Fact]
    public async Task Request_ReplaysInFunctionFormWhenTheToolIsNoLongerOffered()
    {
        // A tool set the operator disabled leaves old calls in the history.
        // With no definition to match, the pair stays a function call and
        // output, which is how they were recorded.
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler, Patch);
        var arguments = JsonSerializer.Serialize(new { patchText = "p" });

        await provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "go"),
                new ToolCall("call_1", "apply_patch", arguments, ToolInputKind.Json),
                new ToolResult("call_1", "done")
            ],
            [ReadTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var input = sent.RootElement.GetProperty("input");
        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Request_KeepsACallWhoseArgumentsAreNotTheOneStringAsAFunctionCall()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}"""));
        using var provider = Provider(handler, Patch);

        await provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "go"),
                new ToolCall("call_1", "apply_patch", "{}", ToolInputKind.Json),
                new ToolResult("call_1", "failed")
            ],
            [PatchTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var input = sent.RootElement.GetProperty("input");
        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Response_WrapsACustomCallAsTheJsonArgumentsTheHostToolReads()
    {
        var patch = "*** Begin Patch\n*** Delete File: a.txt\n*** End Patch";
        var handler = new RecordingHandler(JsonResponse.Create(
            "{\"status\":\"completed\",\"output\":[{\"type\":\"custom_tool_call\",\"call_id\":\"call_1\","
            + "\"name\":\"apply_patch\",\"input\":" + JsonSerializer.Serialize(patch) + "}]}"));
        using var provider = Provider(handler, Patch);

        var response = await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "go")], [PatchTool()]));

        var call = Assert.IsType<ToolCall>(response.Candidates[0].Items[0]);
        Assert.Equal(ToolInputKind.Json, call.InputKind);
        Assert.Equal("call_1", call.CallId);
        using var arguments = JsonDocument.Parse(call.Arguments);
        Assert.Equal(patch, arguments.RootElement.GetProperty("patchText").GetString());
        Assert.Equal(FinishReason.ToolCalls, response.Candidates[0].FinishReason);
    }

    [Fact]
    public async Task Response_KeepsACustomCallToAnUndeclaredToolAsText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"custom_tool_call","call_id":"c","name":"other","input":"raw"}]}"""));
        using var provider = Provider(handler, Patch);

        var response = await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "go")], [ReadTool()]));

        var call = Assert.IsType<ToolCall>(response.Candidates[0].Items[0]);
        Assert.Equal(ToolInputKind.Text, call.InputKind);
        Assert.Equal("raw", call.Arguments);
    }

    [Fact]
    public async Task Stream_EmitsIdentityThenTheWrappedTextOnDone()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch","input":""}}

            data: {"type":"response.custom_tool_call_input.delta","output_index":0,"delta":"*** Begin "}

            data: {"type":"response.custom_tool_call_input.delta","output_index":0,"delta":"Patch"}

            data: {"type":"response.custom_tool_call_input.done","output_index":0,"input":"*** Begin Patch"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch","input":"*** Begin Patch"}}

            data: {"type":"response.completed","response":{"status":"completed","usage":{"input_tokens":4,"output_tokens":2}}}

            """));
        using var provider = Provider(handler, Patch);

        var tools = new List<ChatToolCallDelta>();
        FinishReason? finish = null;
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "go")], [PatchTool()])))
        {
            if (streamEvent is ChatToolCallDelta delta)
            {
                tools.Add(delta);
            }

            if (streamEvent is ChatCandidateCompleted completed)
            {
                finish = completed.FinishReason;
            }
        }

        Assert.Equal(2, tools.Count);
        Assert.Equal("call_1", tools[0].CallIdDelta);
        Assert.Equal("apply_patch", tools[0].NameDelta);
        Assert.Equal(ToolInputKind.Json, tools[0].InputKind);
        using var arguments = JsonDocument.Parse(tools[1].ArgumentsDelta);
        Assert.Equal("*** Begin Patch", arguments.RootElement.GetProperty("patchText").GetString());
        Assert.Equal(FinishReason.ToolCalls, finish);
    }

    [Fact]
    public async Task Stream_FallsBackToHeldDeltasWhenDoneCarriesNoText()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch"}}

            data: {"type":"response.custom_tool_call_input.delta","output_index":0,"delta":"abc"}

            data: {"type":"response.custom_tool_call_input.delta","output_index":0,"delta":"def"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch"}}

            data: {"type":"response.completed","response":{"status":"completed"}}

            """));
        using var provider = Provider(handler, Patch);

        var tools = new List<ChatToolCallDelta>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "go")], [PatchTool()])))
        {
            if (streamEvent is ChatToolCallDelta delta)
            {
                tools.Add(delta);
            }
        }

        using var arguments = JsonDocument.Parse(tools[^1].ArgumentsDelta);
        Assert.Equal("abcdef", arguments.RootElement.GetProperty("patchText").GetString());
    }

    [Fact]
    public async Task Stream_EmitsTheTextOnlyOnceWhenBothDoneEventsCarryIt()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch"}}

            data: {"type":"response.custom_tool_call_input.done","output_index":0,"input":"x"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch","input":"x"}}

            data: {"type":"response.completed","response":{"status":"completed"}}

            """));
        using var provider = Provider(handler, Patch);

        var tools = new List<ChatToolCallDelta>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "go")], [PatchTool()])))
        {
            if (streamEvent is ChatToolCallDelta delta)
            {
                tools.Add(delta);
            }
        }

        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task Stream_HoldsTheTextWhenTheServerFinishesItBeforeNamingTheItem()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.custom_tool_call_input.done","output_index":0,"input":"early"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"custom_tool_call","call_id":"call_1","name":"apply_patch"}}

            data: {"type":"response.completed","response":{"status":"completed"}}

            """));
        using var provider = Provider(handler, Patch);

        var tools = new List<ChatToolCallDelta>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "go")], [PatchTool()])))
        {
            if (streamEvent is ChatToolCallDelta delta)
            {
                tools.Add(delta);
            }
        }

        Assert.All(tools, delta => Assert.Equal(ToolInputKind.Json, delta.InputKind));
        Assert.Equal("apply_patch", tools[0].NameDelta);
        using var arguments = JsonDocument.Parse(tools[^1].ArgumentsDelta);
        Assert.Equal("early", arguments.RootElement.GetProperty("patchText").GetString());
    }

    [Fact]
    public void Binding_MatchesOnlyAToolWithOneStringProperty()
    {
        Assert.True(Patch.Matches(PatchTool()));
        Assert.False(Patch.Matches(ReadTool()));
        Assert.False(Patch.Matches(ToolDefinition.Text("apply_patch", "text")));
        Assert.False(Patch.Matches(ToolDefinition.Json(
            "apply_patch",
            JsonDocument.Parse("""{"type":"object","properties":{"patchText":{"type":"integer"}}}""").RootElement)));
    }

    [Fact]
    public void Binding_UnwrapRejectsAnythingButTheOneString()
    {
        Assert.True(Patch.TryUnwrap("""{"patchText":"abc"}""", out var text));
        Assert.Equal("abc", text);
        Assert.False(Patch.TryUnwrap("{}", out _));
        Assert.False(Patch.TryUnwrap("""{"patchText":3}""", out _));
        Assert.False(Patch.TryUnwrap("not json", out _));
        Assert.False(Patch.TryUnwrap("", out _));
    }

    [Fact]
    public async Task Multimodal_FoldsTheSameWayOnTheWire()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            "{\"status\":\"completed\",\"output\":[{\"type\":\"custom_tool_call\",\"call_id\":\"call_2\","
            + "\"name\":\"apply_patch\",\"input\":\"P\"}]}"));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesMultimodalProvider(
            new ResponsesOptions("key", "gpt-test", BaseUri, textTools: [Patch]),
            http);
        var arguments = JsonSerializer.Serialize(new { patchText = "OLD" });

        var response = await provider.CompleteAsync(new MultimodalChatRequest(
            [
                new MultimodalMessage(MultimodalChatRole.User, [new TextContent("go")]),
                new MultimodalToolCall("call_1", "apply_patch", arguments, ToolInputKind.Json),
                new MultimodalToolResult(
                    "call_1",
                    [new TextContent("done")],
                    MultimodalToolResultStatus.Success)
            ],
            [PatchTool()]));

        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal("custom", sent.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
        var input = sent.RootElement.GetProperty("input");
        Assert.Equal("custom_tool_call", input[1].GetProperty("type").GetString());
        Assert.Equal("OLD", input[1].GetProperty("input").GetString());
        Assert.Equal("custom_tool_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("done", input[2].GetProperty("output").GetString());
        var call = response.Candidates[0].Items.OfType<MultimodalToolCall>().Single();
        Assert.Equal(ToolInputKind.Json, call.InputKind);
        using var wrapped = JsonDocument.Parse(call.Arguments);
        Assert.Equal("P", wrapped.RootElement.GetProperty("patchText").GetString());
    }
}
