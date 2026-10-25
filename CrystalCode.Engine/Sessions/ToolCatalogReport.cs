using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// The effective Plan and Work catalogs reported by <c>/tools</c>, as plain
/// data. It carries no settings object, so a front end can serialize it
/// without exposing provider definitions or credentials.
/// </summary>
public sealed record ToolCatalogReport(
    IReadOnlyList<ToolDefinition> Plan,
    IReadOnlyList<ToolDefinition> Work,
    IReadOnlyList<ExternalToolInfo> External,
    IReadOnlyList<string> ExternalNotes,
    bool ExternalToolsEnabled,
    ExternalToolApprovalSettings ExternalApproval)
{
    public static ToolCatalogReport Create(
        IReadOnlyList<ToolDefinition> plan,
        IReadOnlyList<ToolDefinition> work,
        ExternalCatalog external,
        HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(external);
        ArgumentNullException.ThrowIfNull(settings);
        return new ToolCatalogReport(
            plan,
            work,
            external.Tools,
            external.Notes,
            settings.ExternalTools,
            settings.ExternalToolApproval);
    }
}
