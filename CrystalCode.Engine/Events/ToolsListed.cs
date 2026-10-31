using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Catalogs;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The effective Plan and Work tool catalogs. Format with <c>ToolListText</c> or a richer view.
/// </summary>
public sealed record ToolsListed(ToolCatalogReport Report) : SessionEvent;
