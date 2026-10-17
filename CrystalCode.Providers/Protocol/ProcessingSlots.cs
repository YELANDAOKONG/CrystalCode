using System.Text.Json;

namespace CrystalCode.Providers.Protocol;

/// <summary>
/// Writes service tier and speed onto a protocol that already decided the
/// JSON field. The host does not choose the field name.
/// </summary>
internal static class ProcessingSlots
{
    public static void WriteShared(
        Utf8JsonWriter writer,
        string? serviceTier,
        string? speed,
        string vendorName)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        if (serviceTier is not null && speed is not null)
        {
            throw new NotSupportedException(
                $"{vendorName} accepts only one of service tier and speed.");
        }

        var value = serviceTier ?? speed;
        if (value is not null)
        {
            writer.WriteString("service_tier", value);
        }
    }

    public static void WriteSeparate(Utf8JsonWriter writer, string? serviceTier, string? speed)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (serviceTier is not null)
        {
            writer.WriteString("service_tier", serviceTier);
        }

        if (speed is not null)
        {
            writer.WriteString("speed", speed);
        }
    }

    public static void Reject(string? serviceTier, string? speed, string vendorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        if (serviceTier is not null || speed is not null)
        {
            throw new NotSupportedException(
                $"{vendorName} does not support service tier or speed.");
        }
    }
}
