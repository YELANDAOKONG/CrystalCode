namespace CrystalCode.Server.Security;

/// <summary>A newly issued token. <see cref="Secret"/> is shown once.</summary>
public sealed record IssuedToken(string Id, string Secret, ServerScope Scope, string Label);
