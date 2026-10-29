namespace CrystalCode.Engine.Sessions;

/// <summary>Whether skills are on, and the skills loaded for this session.</summary>
public sealed record SkillCatalogReport(bool Enabled, IReadOnlyList<SkillCatalogEntry> Skills);
