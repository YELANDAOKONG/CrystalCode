namespace CrystalCode.Engine.Sessions.Catalogs;

/// <summary>Whether MCP is on, its servers, and the notes from connecting them.</summary>
public sealed record McpCatalogReport(bool Enabled, IReadOnlyList<McpServerReport> Servers, IReadOnlyList<string> Notes);
