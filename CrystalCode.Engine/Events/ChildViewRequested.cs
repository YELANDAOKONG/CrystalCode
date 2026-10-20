namespace CrystalCode.Engine.Events;

/// <summary>Requests a front-end-only change of transcript focus.</summary>
public sealed record ChildViewRequested(string? ChildSessionId) : SessionEvent;
