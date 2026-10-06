namespace CrystalCode.Display.Transcript;

/// <summary>
/// One transcript row that stays put when later rows arrive or an earlier
/// block changes height. A live tail uses the entry index it will occupy
/// once it is committed.
/// </summary>
internal readonly record struct ScrollMark(int EntryIndex, int Line);
