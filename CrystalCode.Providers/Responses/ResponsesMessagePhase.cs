using System.Text;
using System.Text.Json;

using Crystal.Reasoning;

namespace CrystalCode.Providers.Responses;

internal static class ResponsesMessagePhase
{
    private const string Format = "openai.responses.message_phase";

    // The chat contracts carry opaque provider state but have no phase field.
    // Keep the phase immediately after its assistant message in the transcript.
    public static OpaqueReasoningState? Read(JsonElement item)
    {
        if (item.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String
            && Validate(phase.GetString()) is { } value)
        {
            return new OpaqueReasoningState(Format, Encoding.UTF8.GetBytes(value));
        }

        return null;
    }

    public static string? Get(OpaqueReasoningState? state) =>
        state?.Format == Format ? Validate(Encoding.UTF8.GetString(state.Data.Span)) : null;

    private static string? Validate(string? phase) =>
        phase is "commentary" or "final_answer" ? phase : null;
}
