namespace CrystalCode.Engine.Sessions;

/// <summary>One discovered skill, without its body. <see cref="Source"/> is <c>home</c> or <c>project</c>.</summary>
public sealed record SkillCatalogEntry(string Name, string Description, string Source, string Location);
