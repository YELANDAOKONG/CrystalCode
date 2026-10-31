namespace CrystalCode.Display.Paint;

/// <summary>
/// The part of a removed line and of an added line that differs, found by
/// trimming the text they share at both ends. Each pair is the start and
/// length of the differing run in that line.
/// </summary>
public readonly record struct DiffSpan(int OldStart, int OldLength, int NewStart, int NewLength)
{
    /// <summary>
    /// Returns null when the lines are equal, or share too little to make a
    /// span read as an edit rather than a rewrite.
    /// </summary>
    public static DiffSpan? Between(string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return null;
        }

        var limit = Math.Min(oldText.Length, newText.Length);
        var prefix = 0;
        while (prefix < limit && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < limit - prefix
            && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
        {
            suffix++;
        }

        // Never split a surrogate pair at either edge.
        if (prefix > 0 && prefix < oldText.Length && char.IsLowSurrogate(oldText[prefix]))
        {
            prefix--;
        }

        if (suffix > 0 && char.IsLowSurrogate(oldText[oldText.Length - suffix]))
        {
            suffix--;
        }

        var shared = prefix + suffix;
        if (shared * 2 < Math.Max(oldText.Length, newText.Length))
        {
            return null;
        }

        return new DiffSpan(prefix, oldText.Length - prefix - suffix, prefix, newText.Length - prefix - suffix);
    }
}
