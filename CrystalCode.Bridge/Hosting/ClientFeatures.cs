using System.Text.Json;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The client capabilities Bridge acts on, read once from <c>initialize</c>.
/// Anything not listed here is ignored.
/// </summary>
/// <param name="Notices"><c>session.notices</c>: notes, errors, and retries become <c>notice</c>.</param>
/// <param name="Forms"><c>elicitation.form</c>: questions, trust, and choices become forms.</param>
/// <param name="BooleanOptions"><c>session.configOptions.boolean</c>: switches are boolean options.</param>
/// <param name="Compaction"><c>session.compaction</c>: compaction becomes <c>compaction_update</c>.</param>
/// <param name="Subagents">
/// <c>_meta.crystal.subagents</c> (not ACP): children are restricted sessions
/// in the subagent RFD shapes. A root <c>subagents</c> is not read.
/// </param>
/// <param name="Extensions">
/// <c>_meta.crystal.extensions</c> (not ACP): Bridge sends <c>_crystal/*</c>
/// notifications. <c>_crystal/*</c> requests are answered either way.
/// </param>
internal sealed record ClientFeatures(
    bool Notices,
    bool Forms = false,
    bool BooleanOptions = false,
    bool Compaction = false,
    bool Subagents = false,
    bool Extensions = false)
{
    public static ClientFeatures None { get; } = new(false);

    public static ClientFeatures Read(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("clientCapabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Object)
        {
            return None;
        }

        var session = Child(capabilities, "session");
        var elicitation = Child(capabilities, "elicitation");
        var configOptions = session is { } value ? Child(value, "configOptions") : null;
        var crystal = Child(capabilities, "_meta") is { } meta ? Child(meta, "crystal") : null;
        return new ClientFeatures(
            session is { } notices && Child(notices, "notices") is not null,
            elicitation is { } forms && Child(forms, "form") is not null,
            configOptions is { } options && Child(options, "boolean") is not null,
            session is { } compaction && Child(compaction, "compaction") is not null,
            crystal is { } opted && Child(opted, "subagents") is not null,
            crystal is { } extended && Child(extended, "extensions") is not null);
    }

    /// <summary>An object child; ACP advertises a capability with <c>{}</c>.</summary>
    private static JsonElement? Child(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object ? child : null;
}
