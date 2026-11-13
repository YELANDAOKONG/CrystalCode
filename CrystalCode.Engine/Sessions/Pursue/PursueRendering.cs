namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// The checker's view of the conversation. <see cref="Entries"/> are the
/// numbers it may cite. <see cref="Fits"/> is false when even the newest
/// entry cannot be shown.
/// </summary>
internal sealed record PursueRendering(string Text, IReadOnlySet<int> Entries, bool Fits);
