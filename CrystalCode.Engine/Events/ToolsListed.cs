using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The effective Plan and Work tool catalogs. Format with <c>ToolListText</c> or a richer view.
/// </summary>
public sealed record ToolsListed(
    IReadOnlyList<ToolDefinition> Plan,
    IReadOnlyList<ToolDefinition> Work,
    ExternalCatalog External,
    HarnessSettings Settings) : SessionEvent;
