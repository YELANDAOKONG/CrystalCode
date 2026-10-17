global using static CrystalCode.Providers.Tests.JsonToolCalls;

using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Providers.Tests;

/// <summary>
/// Creates JSON-input tool calls for existing tests. Text tool input is not
/// enabled in CrystalCode yet.
/// </summary>
internal static class JsonToolCalls
{
    public static ToolCall JsonToolCall(
        string callId,
        string name,
        string arguments) =>
        new(callId, name, arguments, ToolInputKind.Json);

    public static MultimodalToolCall JsonMultimodalToolCall(
        string callId,
        string name,
        string arguments,
        IEnumerable<MultimodalContent>? contents = null) =>
        new(callId, name, arguments, ToolInputKind.Json, contents);

    public static ChatToolCallDelta JsonChatToolCallDelta(
        int candidateIndex,
        int itemIndex,
        string callIdDelta,
        string nameDelta,
        string argumentsDelta) =>
        new(
            candidateIndex,
            itemIndex,
            callIdDelta,
            nameDelta,
            argumentsDelta,
            ToolInputKind.Json);

    public static MultimodalToolCallDelta JsonMultimodalToolCallDelta(
        int candidateIndex,
        int itemIndex,
        string callIdDelta,
        string nameDelta,
        string argumentsDelta) =>
        new(
            candidateIndex,
            itemIndex,
            callIdDelta,
            nameDelta,
            argumentsDelta,
            ToolInputKind.Json);
}
