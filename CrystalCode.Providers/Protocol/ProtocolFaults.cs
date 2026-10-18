using System.Text.Json;

using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.Protocol;

internal static class ProtocolFaults
{
    public static bool TryCreate(
        JsonElement root,
        string vendorName,
        string phase,
        Func<string, string?, Exception?, Exception> create,
        out Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(create);
        exception = null!;
        if (!CompatibleWire.TryReadError(root, out var code, out var providerMessage))
        {
            return false;
        }

        exception = create(Describe(vendorName, phase, code, providerMessage), code, null);
        return true;
    }

    public static Exception FromBody(
        string body,
        string vendorName,
        string phase,
        Func<string, string?, Exception?, Exception> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(create);
        if (CompatibleWire.TryReadError(body, out var code, out var providerMessage))
        {
            return create(Describe(vendorName, phase, code, providerMessage), code, null);
        }

        return create($"{vendorName} stream did not contain an event stream.", null, null);
    }

    public static string Describe(
        string vendorName,
        string phase,
        string? code,
        string? providerMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        var message = code is null
            ? $"{vendorName} {phase} failed."
            : $"{vendorName} {phase} failed (code: {code}).";
        return string.IsNullOrWhiteSpace(providerMessage)
            ? message
            : $"{message} {providerMessage}";
    }
}
