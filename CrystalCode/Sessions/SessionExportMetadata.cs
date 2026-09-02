using System.Text;

namespace CrystalCode.Sessions;

/// <summary>
/// Runtime facts attached to a session export.
/// </summary>
public sealed record SessionExportMetadata(
    string SessionId,
    string Workspace,
    string Provider,
    string Model,
    string PromptSet,
    bool PlanMode,
    DateTimeOffset ExportedUtc)
{
    public string ModelLine => Provider + " / " + Model;
}
