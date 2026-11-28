using TrayTranslator.Core.AiActions;

namespace TrayTranslator.Tests.AiActions;

public class AiPromptTests
{
    [Fact]
    public void Wraps_the_instruction_and_the_text_in_tags()
    {
        var message = AiPrompt.UserMessage("  Polish it  ", "hello world");
        Assert.Equal("<instruction>\nPolish it\n</instruction>\n\n<selected_text>\nhello world\n</selected_text>", message);
    }

    [Theory]
    [InlineData("Hello.", "hola.", "Hello.")]
    [InlineData("\nHello.\n\n", "hola.", "Hello.")]
    [InlineData("Hello.", "hola.\r\n", "Hello.\r\n")]
    [InlineData("Hello.", "  hola.", "  Hello.")]
    [InlineData("Line one\nLine two\n", "\tuno\ndos\n", "\tLine one\nLine two\n")]
    public void Keeps_the_selections_surrounding_whitespace(string reply, string selected, string expected)
    {
        Assert.Equal(expected, AiPrompt.ToReplacement(reply, selected));
    }
}
