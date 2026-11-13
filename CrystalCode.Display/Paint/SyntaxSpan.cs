namespace CrystalCode.Display.Paint;

/// <summary>A token range in the fenced block's original UTF-16 text, before tab expansion or wrapping.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, string Kind);
