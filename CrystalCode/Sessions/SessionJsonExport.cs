using System.Text.Json;
using CrystalCode.Home;

namespace CrystalCode.Sessions;

/// <summary>
/// Writes structured session export JSON.
/// </summary>
public static class SessionJsonExport
{
    public static string Render(
        SessionExportMetadata metadata,
        SessionDocument session,
        string? systemText)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(session);
        var document = new SessionExportDocument
        {
            ExportedUtc = metadata.ExportedUtc,
            Session = session,
            Runtime = new SessionExportRuntimeDocument
            {
                Workspace = metadata.Workspace,
                Provider = metadata.Provider,
                Model = metadata.Model,
                ModelLine = metadata.ModelLine,
                PromptSet = metadata.PromptSet,
                PlanMode = metadata.PlanMode
            },
            System = string.IsNullOrWhiteSpace(systemText) ? null : systemText.Trim()
        };

        return JsonSerializer.Serialize(document, HomeJson.Options);
    }
}
