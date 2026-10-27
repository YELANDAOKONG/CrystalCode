using System.Text.Json;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The client capabilities Bridge acts on, read once from <c>initialize</c>.
/// Anything not listed here is ignored.
/// </summary>
internal sealed record ClientFeatures(bool Notices)
{
    public static ClientFeatures None { get; } = new(false);

    public static ClientFeatures Read(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("clientCapabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Object
            || !capabilities.TryGetProperty("session", out var session)
            || session.ValueKind != JsonValueKind.Object)
        {
            return None;
        }

        return new ClientFeatures(
            session.TryGetProperty("notices", out var notices) && notices.ValueKind == JsonValueKind.Object);
    }
}
