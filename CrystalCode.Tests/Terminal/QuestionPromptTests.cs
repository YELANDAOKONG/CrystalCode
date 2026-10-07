using CrystalCode.Display.Input;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class QuestionPromptTests
{
    [Fact]
    public void IsPlainChoiceKey_IgnoresCtrlJ()
    {
        var ctrlJ = new InputKey(ConsoleKey.J, '\n', ConsoleModifiers.Control);
        var plainJ = new InputKey(ConsoleKey.J, 'j', ConsoleModifiers.None);
        var shiftedJ = new InputKey(ConsoleKey.J, 'J', ConsoleModifiers.Shift);

        Assert.False(QuestionPrompt.IsPlainChoiceKey(ctrlJ));
        Assert.True(QuestionPrompt.IsPlainChoiceKey(plainJ));
        Assert.True(QuestionPrompt.IsPlainChoiceKey(shiftedJ));
    }
}
