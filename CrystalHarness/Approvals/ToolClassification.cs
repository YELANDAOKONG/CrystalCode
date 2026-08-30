namespace CrystalHarness.Approvals;

/// <summary>
/// Risk and authority assigned to one tool call.
/// </summary>
public sealed record ToolClassification(
    Risk Risk,
    Authority Authority,
    string Summary);
