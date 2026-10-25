namespace CrystalCode.Engine.Sessions;

/// <summary>
/// The result of <see cref="CodingSession.CloseAsync"/>.
/// </summary>
/// <param name="SessionId">The session id that was closing.</param>
/// <param name="Saved">True when the conversation had content and was saved.</param>
/// <param name="Hint">Plain-text resume instructions for a terminal.</param>
public sealed record SessionClosure(string SessionId, bool Saved, string Hint);
