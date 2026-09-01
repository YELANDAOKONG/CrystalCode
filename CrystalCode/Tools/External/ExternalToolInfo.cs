namespace CrystalCode.Tools.External;

/// <summary>
/// Display and policy metadata for one loaded external tool.
/// </summary>
public sealed record ExternalToolInfo(
    string Name,
    string SetName,
    ExternalToolSource Source,
    ExternalCatalogSelection Catalogs,
    ExternalApprovalMode DeclaredApproval,
    ExternalApprovalMode EffectiveApproval);
