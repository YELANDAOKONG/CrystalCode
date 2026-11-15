namespace CrystalCode.Engine.Authentication;

/// <summary>
/// The outcome of removing a saved login.
/// </summary>
/// <param name="Removed">Whether a saved login existed and was deleted.</param>
/// <param name="Warning">A note when the remote session end was not confirmed.</param>
public sealed record LogoutResult(bool Removed, string? Warning);
