namespace CrystalCode.Engine.Events;

/// <summary>A local context estimate, never provider-reported token usage.</summary>
public sealed record ContextEstimated(int Tokens) : SessionEvent;
