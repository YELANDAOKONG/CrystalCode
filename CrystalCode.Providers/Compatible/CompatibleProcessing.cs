namespace CrystalCode.Providers.Compatible;

/// <summary>
/// How a Chat Completions profile treats the host's service-tier and speed slots.
/// </summary>
internal enum CompatibleProcessing
{
    /// <summary>Either slot is a protocol error. Nothing is written.</summary>
    Unsupported,

    /// <summary>
    /// Either slot is written to <c>service_tier</c>. Both slots set is an error
    /// because the protocol has one field.
    /// </summary>
    SharedServiceTier
}
