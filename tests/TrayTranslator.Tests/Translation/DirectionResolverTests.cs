using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Translation;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Translation;

public class DirectionResolverTests
{
    [Fact]
    public void Source_A_translates_to_B()
    {
        var d = DirectionResolver.Resolve(new DetectionResult("en", 0.9), Lang.En, Lang.Es);
        Assert.Equal(("en", "es"), (d.Source?.Code, d.Target.Code));
    }

    [Fact]
    public void Source_B_translates_to_A()
    {
        var d = DirectionResolver.Resolve(new DetectionResult("es", 0.9), Lang.En, Lang.Es);
        Assert.Equal(("es", "en"), (d.Source?.Code, d.Target.Code));
    }

    [Fact]
    public void Other_language_translates_to_A_with_engine_detection()
    {
        var d = DirectionResolver.Resolve(new DetectionResult("fr", 1.0), Lang.En, Lang.Es);
        Assert.Null(d.Source);
        Assert.Equal("en", d.Target.Code);
    }

    [Theory]
    [InlineData("es", 0.49)]
    [InlineData("en", 0.1)]
    [InlineData("es", 0.0)]
    public void Low_confidence_translates_to_A_with_engine_detection(string code, double confidence)
    {
        var d = DirectionResolver.Resolve(new DetectionResult(code, confidence), Lang.En, Lang.Es);
        Assert.Null(d.Source);
        Assert.Equal("en", d.Target.Code);
    }

    [Fact]
    public void Unknown_translates_to_A_with_engine_detection()
    {
        var d = DirectionResolver.Resolve(DetectionResult.Unknown, Lang.En, Lang.Es);
        Assert.Null(d.Source);
        Assert.Equal("en", d.Target.Code);
    }

    [Fact]
    public void Confidence_exactly_at_threshold_counts()
    {
        var d = DirectionResolver.Resolve(new DetectionResult("es", DirectionResolver.DefaultConfidenceThreshold), Lang.En, Lang.Es);
        Assert.Equal("es", d.Source?.Code);
    }

    [Fact]
    public void Works_with_a_reversed_pair()
    {
        var d = DirectionResolver.Resolve(new DetectionResult("es", 0.9), Lang.Es, Lang.En);
        Assert.Equal(("es", "en"), (d.Source?.Code, d.Target.Code));

        var other = DirectionResolver.Resolve(new DetectionResult("de", 0.9), Lang.Es, Lang.En);
        Assert.Equal("es", other.Target.Code);
    }

    [Fact]
    public void Swap_reverses_the_direction()
    {
        var swapped = DirectionResolver.Swap(new TranslationDirection(Lang.Es, Lang.En), Lang.En, Lang.Es);
        Assert.Equal(("en", "es"), (swapped.Source?.Code, swapped.Target.Code));

        var back = DirectionResolver.Swap(swapped, Lang.En, Lang.Es);
        Assert.Equal(("es", "en"), (back.Source?.Code, back.Target.Code));
    }

    [Fact]
    public void Swap_of_an_auto_detected_direction_treats_the_text_as_the_old_target()
    {
        var swapped = DirectionResolver.Swap(new TranslationDirection(null, Lang.En), Lang.En, Lang.Es);
        Assert.Equal(("en", "es"), (swapped.Source?.Code, swapped.Target.Code));
    }

    [Fact]
    public void Picking_a_target_keeps_the_shown_source()
    {
        var d = DirectionResolver.ToTarget(Lang.Es, Lang.Fr);
        Assert.Equal(("es", "fr"), (d.Source?.Code, d.Target.Code));
    }

    [Fact]
    public void Picking_the_source_language_lets_the_engine_detect()
    {
        var d = DirectionResolver.ToTarget(Lang.Es, Lang.Es);
        Assert.Null(d.Source);
        Assert.Equal("es", d.Target.Code);
    }

    [Fact]
    public void Picking_a_target_with_an_unknown_source_lets_the_engine_detect()
    {
        var d = DirectionResolver.ToTarget(null, Lang.De);
        Assert.Null(d.Source);
        Assert.Equal("de", d.Target.Code);
    }
}
