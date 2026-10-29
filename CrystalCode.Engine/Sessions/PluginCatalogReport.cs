namespace CrystalCode.Engine.Sessions;

/// <summary>Whether plugin discovery is on, every plugin found, and the load notes.</summary>
public sealed record PluginCatalogReport(bool Enabled, IReadOnlyList<PluginCatalogEntry> Plugins, IReadOnlyList<string> Notes);
