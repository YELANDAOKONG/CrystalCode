using System.Text.Json;

using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Events;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// Holds the structured values of the next report of one kind while a
/// <c>_crystal/status</c>, <c>_crystal/stats</c>, or <c>_crystal/tools</c>
/// request runs its command, so the request can answer with them. Disposing
/// stops capturing.
/// </summary>
internal sealed class ReportCapture(string kind, Action<ReportCapture> release) : IDisposable
{
    public string Kind { get; } = kind;

    /// <summary>The result object, or null when the report has not arrived.</summary>
    public Action<Utf8JsonWriter>? Values { get; private set; }

    public bool Accept(SessionEvent sessionEvent)
    {
        Action<Utf8JsonWriter>? values = (Kind, sessionEvent) switch
        {
            ("status", StatusReported status) => writer =>
            {
                writer.WriteStartObject();
                writer.WriteBoolean("full", status.Full);
                CrystalWire.Status(writer, "status", status.Status);
                writer.WriteEndObject();
            },
            ("stats", StatsReported stats) => writer =>
            {
                writer.WriteStartObject();
                CrystalWire.Stats(writer, "report", stats.Report, stats.AllWorkspaces);
                writer.WriteEndObject();
            },
            ("tools", ToolsListed tools) => writer =>
            {
                writer.WriteStartObject();
                CrystalWire.ToolCatalog(writer, "report", tools.Report);
                writer.WriteEndObject();
            },
            _ => null
        };
        if (values is null || Values is not null)
        {
            return false;
        }

        Values = values;
        return true;
    }

    public void Dispose() => release(this);
}
