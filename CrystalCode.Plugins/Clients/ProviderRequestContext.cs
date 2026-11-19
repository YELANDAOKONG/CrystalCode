namespace CrystalCode.Plugins.Clients;

/// <summary>Host identity and diagnostic sink captured for one provider request.</summary>
public sealed record ProviderRequestContext(
    string SessionId,
    string? ParentSessionId,
    Action<ProviderRequestUsage> ReportUsage,
    bool IsAuxiliary = false);
