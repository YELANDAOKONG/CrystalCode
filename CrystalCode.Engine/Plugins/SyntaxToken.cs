namespace CrystalCode.Engine.Plugins;

public readonly record struct SyntaxToken(int Start, int Length, string Kind);
