using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Prompts;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class ExtendedApprovalPromptTests
{
    [Fact]
    public void UserText_IncludesThePrimaryVerdictWhenAttached()
    {
        var request = Request(new ApprovalReviewVerdict(
            "allow",
            ReviewRiskLevel.Medium,
            ReviewAuthorization.High,
            "The user asked for the build."));

        var text = ExtendedApprovalPrompt.UserText(request);

        Assert.Contains("Outcome: allow", text, StringComparison.Ordinal);
        Assert.Contains("Risk level: medium", text, StringComparison.Ordinal);
        Assert.Contains("User authorization: high", text, StringComparison.Ordinal);
        Assert.Contains("Rationale: The user asked for the build.", text, StringComparison.Ordinal);
        Assert.Contains("Tool: bash", text, StringComparison.Ordinal);
        Assert.Contains("[user] Build it.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UserText_SaysThePrimaryVerdictIsHiddenWhenNotAttached()
    {
        var text = ExtendedApprovalPrompt.UserText(Request(primary: null));

        Assert.Contains(ExtendedApprovalPrompt.PrimaryReviewOmitted, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Outcome:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UserText_UsesAnOperatorTemplate()
    {
        var text = ExtendedApprovalPrompt.UserText(
            Request(ApprovalReviewVerdict.Allow("fine")),
            facts: null,
            template: "custom {{tool_name}} / {{primary_review}}");

        Assert.StartsWith("custom bash / Outcome: allow", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemText_DefaultsToAllowAndKeepsTheVerdictShape()
    {
        var text = ExtendedApprovalPrompt.ComposeSystem(
            PromptContext.InstructionsOnly(string.Empty));

        Assert.Contains("Default to allow.", text, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"allow\"|\"deny\"|\"ask\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{product_name}}", text, StringComparison.Ordinal);
    }

    private static ApprovalReviewRequest Request(ApprovalReviewVerdict? primary) =>
        new(
            new ToolCall("1", "bash", """{"command":"dotnet build"}""", ToolInputKind.Json),
            new ToolClassification(Risk.Write, Authority.Workspace, "Run a command"),
            "[user] Build it.",
            primary);
}
