using CrystalCode.Sessions;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class QuestionFlowTests
{
    [Fact]
    public void SelectCurrent_SingleChoice_SubmitsImmediately()
    {
        var flow = new QuestionFlow(
            [Question(multiple: false, custom: true, "Build", "Tests")]);

        var action = flow.SelectCurrent();

        Assert.True(action.Submit);
        Assert.Equal(["Build"], flow.Answers[0]);
    }

    [Fact]
    public void SelectCurrent_MultipleChoice_TogglesAnswersUntilConfirm()
    {
        var flow = new QuestionFlow(
            [Question(multiple: true, custom: false, "Build", "Tests")]);

        flow.SelectCurrent();
        flow.MoveSelection(1);
        flow.SelectCurrent();
        flow.MoveTab(1);

        Assert.True(flow.IsConfirm);
        Assert.Equal(["Build", "Tests"], flow.Answers[0]);
        Assert.True(flow.SelectCurrent().Submit);
    }

    [Fact]
    public void SaveCustom_MultipleChoice_AddsAndReplacesCustomAnswer()
    {
        var flow = new QuestionFlow(
            [Question(multiple: true, custom: true, "Build")]);
        flow.MoveSelection(1);

        var first = flow.SelectCurrent();
        flow.SaveCustom("  Local check  ");
        var toggledOff = flow.SelectCurrent();
        var second = flow.SelectCurrent();
        flow.SaveCustom("Other check");

        Assert.True(first.EditCustom);
        Assert.False(toggledOff.EditCustom);
        Assert.True(second.EditCustom);
        Assert.Equal(["Other check"], flow.Answers[0]);
    }

    [Fact]
    public void SelectCurrent_MultipleQuestions_AdvancesToConfirmation()
    {
        var flow = new QuestionFlow(
            [
                Question(multiple: false, custom: false, "Build"),
                Question(multiple: false, custom: false, "Tests")
            ]);

        flow.SelectCurrent();
        Assert.Equal(1, flow.Tab);
        flow.SelectCurrent();

        Assert.True(flow.IsConfirm);
        Assert.Equal(["Build"], flow.Answers[0]);
        Assert.Equal(["Tests"], flow.Answers[1]);
    }

    private static UserQuestion Question(
        bool multiple,
        bool custom,
        params string[] labels) =>
        new(
            "Choice",
            "What should happen?",
            labels.Select(label => new QuestionOption(label, $"Choose {label}.")).ToArray(),
            multiple,
            custom);
}
