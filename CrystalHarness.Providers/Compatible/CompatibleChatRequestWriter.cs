using System.Text.Json;

using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalHarness.Providers.Compatible;

internal static class CompatibleChatRequestWriter
{
    public static byte[] Write(
        CompatibleProfile profile,
        CompatibleOptions options,
        ChatRequest request,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Items.Count == 0)
        {
            throw new ArgumentException(
                $"{profile.VendorName} chat requires at least one transcript item.",
                nameof(request));
        }

        using var streamBuffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(streamBuffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", options.Model);
            WriteMessages(writer, profile, request.Items);
            WriteTools(writer, request.Tools);
            WriteReasoning(writer, profile, request.Reasoning);
            WriteTokenLimit(writer, profile, options);

            if (options.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (options.TopP is { } topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            writer.WriteBoolean("stream", stream);
            if (stream)
            {
                writer.WritePropertyName("stream_options");
                writer.WriteStartObject();
                writer.WriteBoolean("include_usage", true);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return streamBuffer.ToArray();
    }

    private static void WriteTokenLimit(
        Utf8JsonWriter writer,
        CompatibleProfile profile,
        CompatibleOptions options)
    {
        if (options.MaxTokens is not { } maxTokens)
        {
            return;
        }

        switch (profile.TokenLimit)
        {
            case CompatibleTokenLimit.MaxTokens:
                writer.WriteNumber("max_tokens", maxTokens);
                break;
            case CompatibleTokenLimit.MaxCompletionTokens:
                writer.WriteNumber("max_completion_tokens", maxTokens);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    profile.TokenLimit,
                    "Unknown token-limit style.");
        }
    }

    private static void WriteMessages(
        Utf8JsonWriter writer,
        CompatibleProfile profile,
        IReadOnlyList<ChatItem> items)
    {
        writer.WritePropertyName("messages");
        writer.WriteStartArray();

        AssistantBuffer? assistant = null;
        foreach (var item in items)
        {
            switch (item)
            {
                case ChatMessage message:
                    WriteChatMessage(writer, profile, ref assistant, message);
                    break;
                case ChatReasoningItem reasoning:
                    AppendReasoning(writer, profile, ref assistant, reasoning);
                    break;
                case ToolCall toolCall:
                    assistant ??= new AssistantBuffer();
                    assistant.ToolCalls.Add(toolCall);
                    break;
                case ToolResult toolResult:
                    FlushAssistant(writer, ref assistant);
                    WriteToolMessage(writer, toolResult);
                    break;
                default:
                    throw new NotSupportedException(
                        $"{profile.VendorName} does not support chat item type {item.GetType().Name}.");
            }
        }

        FlushAssistant(writer, ref assistant);
        writer.WriteEndArray();
    }

    private static void WriteChatMessage(
        Utf8JsonWriter writer,
        CompatibleProfile profile,
        ref AssistantBuffer? assistant,
        ChatMessage message)
    {
        if (message.Role == ChatRole.Assistant)
        {
            if (assistant is { HasContent: true })
            {
                FlushAssistant(writer, ref assistant);
            }

            assistant ??= new AssistantBuffer();
            assistant.Content = message.Text;
            assistant.HasContent = true;
            return;
        }

        FlushAssistant(writer, ref assistant);

        if (message.Role != ChatRole.System && message.Role != ChatRole.User)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not support chat role '{message.Role.Value}'.");
        }

        writer.WriteStartObject();
        writer.WriteString("role", message.Role.Value);
        writer.WriteString("content", message.Text);
        writer.WriteEndObject();
    }

    private static void AppendReasoning(
        Utf8JsonWriter writer,
        CompatibleProfile profile,
        ref AssistantBuffer? assistant,
        ChatReasoningItem reasoning)
    {
        if (!profile.WriteReasoningContent)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} cannot replay reasoning blocks on Chat Completions.");
        }

        if (assistant is { HasReasoning: true })
        {
            throw new NotSupportedException(
                $"{profile.VendorName} accepts one reasoning block at the start of an assistant turn.");
        }

        if (assistant is { HasContent: true } or { ToolCalls.Count: > 0 })
        {
            FlushAssistant(writer, ref assistant);
        }

        assistant ??= new AssistantBuffer();
        assistant.ReasoningContent = CompatibleWire.ReadReasoningContent(profile, reasoning.Content);
        assistant.HasReasoning = true;
    }

    private static void FlushAssistant(
        Utf8JsonWriter writer,
        ref AssistantBuffer? assistant)
    {
        if (assistant is null)
        {
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("role", ChatRole.Assistant.Value);

        if (assistant.HasReasoning)
        {
            writer.WriteString("reasoning_content", assistant.ReasoningContent);
        }

        if (assistant.HasContent)
        {
            writer.WriteString("content", assistant.Content);
        }
        else if (assistant.HasReasoning || assistant.ToolCalls.Count > 0)
        {
            writer.WriteNull("content");
        }

        if (assistant.ToolCalls.Count > 0)
        {
            writer.WritePropertyName("tool_calls");
            writer.WriteStartArray();
            foreach (var toolCall in assistant.ToolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", toolCall.CallId);
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                writer.WriteString("name", toolCall.Name);
                writer.WriteString("arguments", toolCall.Arguments);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        assistant = null;
    }

    private static void WriteToolMessage(Utf8JsonWriter writer, ToolResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("role", "tool");
        writer.WriteString("tool_call_id", result.CallId);
        writer.WriteString("content", result.Text);
        writer.WriteEndObject();
    }

    private static void WriteTools(
        Utf8JsonWriter writer,
        IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("tools");
        writer.WriteStartArray();
        foreach (var tool in tools)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            if (tool.Description is not null)
            {
                writer.WriteString("description", tool.Description);
            }

            writer.WritePropertyName("parameters");
            tool.InputSchema.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteReasoning(
        Utf8JsonWriter writer,
        CompatibleProfile profile,
        ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return;
        }

        if (reasoning.TokenBudget is not null)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not support a reasoning token budget.");
        }

        var thinking = ResolveThinking(profile, reasoning);
        var effort = ResolveEffort(profile, reasoning.Effort, thinking);

        if (thinking is not null && profile.WriteThinkingObject)
        {
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", thinking);
            writer.WriteEndObject();
        }

        if (effort is not null)
        {
            writer.WriteString("reasoning_effort", effort);
        }
    }

    private static string? ResolveThinking(CompatibleProfile profile, ReasoningOptions reasoning)
    {
        var mode = reasoning.Mode;
        var output = reasoning.Output;

        if (output == ReasoningOutput.Summary)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not provide reasoning summaries on Chat Completions.");
        }

        if (mode is not null
            && mode != ReasoningMode.Automatic
            && mode != ReasoningMode.Enabled
            && mode != ReasoningMode.Disabled)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not support reasoning mode '{mode.Value}'.");
        }

        if (output is not null
            && output != ReasoningOutput.None
            && output != ReasoningOutput.Full)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not support reasoning output '{output.Value}'.");
        }

        if (mode == ReasoningMode.Disabled && output == ReasoningOutput.Full)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} cannot disable thinking while requesting full reasoning output.");
        }

        if (mode == ReasoningMode.Enabled && output == ReasoningOutput.None)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} cannot enable thinking while requesting no reasoning output.");
        }

        if (mode == ReasoningMode.Disabled || output == ReasoningOutput.None)
        {
            return "disabled";
        }

        if (mode == ReasoningMode.Enabled || output == ReasoningOutput.Full)
        {
            return "enabled";
        }

        return null;
    }

    private static string? ResolveEffort(
        CompatibleProfile profile,
        ReasoningEffort? effort,
        string? thinking)
    {
        if (effort is null)
        {
            return thinking == "disabled" && !profile.WriteThinkingObject
                ? "none"
                : null;
        }

        if (thinking == "disabled")
        {
            throw new NotSupportedException(
                $"{profile.VendorName} does not apply reasoning effort when thinking is disabled.");
        }

        if (effort == ReasoningEffort.Minimal)
        {
            if (!profile.SupportsMinimalEffort)
            {
                throw new NotSupportedException(
                    $"{profile.VendorName} does not support reasoning effort '{effort.Value}'.");
            }

            return "minimal";
        }

        if (effort == ReasoningEffort.Low)
        {
            return "low";
        }

        if (effort == ReasoningEffort.Medium)
        {
            return "medium";
        }

        if (effort == ReasoningEffort.High)
        {
            return "high";
        }

        if (effort == ReasoningEffort.Maximum)
        {
            return profile.MaximumEffortValue;
        }

        throw new NotSupportedException(
            $"{profile.VendorName} does not support reasoning effort '{effort.Value}'.");
    }

    private sealed class AssistantBuffer
    {
        public string? ReasoningContent { get; set; }

        public bool HasReasoning { get; set; }

        public string? Content { get; set; }

        public bool HasContent { get; set; }

        public List<ToolCall> ToolCalls { get; } = [];
    }
}
