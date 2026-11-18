using TrayTranslator.Core.Versioning;

namespace TrayTranslator.Tests.Versioning;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0)]
    [InlineData("v1.10.3", 1, 10, 3)]
    [InlineData(" 2.0.1 ", 2, 0, 1)]
    [InlineData("0.2.0+abc123", 0, 2, 0)]
    public void Parses_major_minor_patch(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("Unreleased")]
    [InlineData("1.-2.3")]
    public void Rejects_anything_else(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void Compares_numerically()
    {
        Assert.True(new AppVersion(0, 10, 0) > new AppVersion(0, 9, 0));
        Assert.True(new AppVersion(1, 0, 0) > new AppVersion(0, 99, 99));
        Assert.True(new AppVersion(0, 2, 1) > new AppVersion(0, 2, 0));
        Assert.Equal("0.2.0", new AppVersion(0, 2, 0).ToString());
    }

    [Theory]
    [InlineData("0.1.0", true, true)]   // updated from 0.1.0
    [InlineData(null, true, true)]      // updated from a build that did not record versions
    [InlineData("0.2.0", true, false)]  // same version
    [InlineData("0.3.0", true, false)]  // went back to an older version
    [InlineData(null, false, false)]    // fresh install, setup not done
    [InlineData("0.1.0", false, false)]
    public void Announces_only_after_an_update(string? lastSeen, bool firstRunCompleted, bool expected)
    {
        Assert.Equal(expected, UpdateAnnouncement.ShouldAnnounce(lastSeen, new AppVersion(0, 2, 0), firstRunCompleted));
    }
}
