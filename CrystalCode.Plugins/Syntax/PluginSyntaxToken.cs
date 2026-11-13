namespace CrystalCode.Plugins.Syntax;

public readonly record struct PluginSyntaxToken(int Start, int Length, PluginSyntaxKind Kind);
