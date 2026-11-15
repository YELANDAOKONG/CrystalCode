namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The verified account an ID token names.
/// </summary>
internal sealed record IdTokenIdentity(string Issuer, string Subject, string? Email);
