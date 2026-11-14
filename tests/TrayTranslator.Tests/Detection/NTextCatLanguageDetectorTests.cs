using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.Tests.Detection;

public class NTextCatLanguageDetectorTests
{
    private readonly NTextCatLanguageDetector _detector = new();

    [Theory]
    [InlineData("La reunión se ha movido al jueves por la tarde.", "es")]
    [InlineData("¿Dónde está el baño?", "es")]
    [InlineData("Por favor confirme su asistencia a la reunión de mañana.", "es")]
    [InlineData("The meeting has been moved to Thursday afternoon.", "en")]
    [InlineData("I need you to review the contract before sending it to the client, please.", "en")]
    [InlineData("Where is the bathroom?", "en")]
    [InlineData("Bonjour tout le monde, comment allez-vous?", "fr")]
    [InlineData("Obrigado pela ajuda, até amanhã", "pt")]
    public void Detects_sentences_confidently(string text, string expected)
    {
        var result = _detector.Detect(text);

        Assert.Equal(expected, result.LanguageCode);
        Assert.True(result.Confidence >= DirectionResolver.DefaultConfidenceThreshold, $"confidence {result.Confidence}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("12345 67890")]
    [InlineData("sí")]
    [InlineData("  ...  ")]
    public void Too_little_text_is_unknown(string text)
    {
        Assert.Equal(DetectionResult.Unknown, _detector.Detect(text));
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("Hola")]
    [InlineData("Email sent")]
    [InlineData("Merci beaucoup")]
    public void Short_ambiguous_text_has_low_confidence(string text)
    {
        Assert.True(_detector.Detect(text).Confidence < DirectionResolver.DefaultConfidenceThreshold);
    }

    [Fact]
    public void Long_text_is_handled()
    {
        var text = string.Concat(Enumerable.Repeat("Esta es una frase en español para probar textos largos. ", 400));
        Assert.Equal("es", _detector.Detect(text).LanguageCode);
    }
}
