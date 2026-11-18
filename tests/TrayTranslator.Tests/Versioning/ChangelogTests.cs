using System.Text.RegularExpressions;
using TrayTranslator.Core.Versioning;

namespace TrayTranslator.Tests.Versioning;

public partial class ChangelogTests
{
    private const string Sample = """
        # Changelog

        Intro text that belongs to no version.

        ## [Unreleased]
        - Not released yet

        ## [0.10.0] - Unreleased

        Big one.

        ### Added
        - First feature, written over
          two lines.
        - Second feature

        ### Fixed
        - A bug

        ## [0.9.0] - 2026-11-15

        - A bullet with no subheading
        """;

    [Fact]
    public void Parses_versions_newest_first_and_skips_unreleased()
    {
        var entries = Changelog.Parse(Sample);

        Assert.Equal([new AppVersion(0, 10, 0), new AppVersion(0, 9, 0)], entries.Select(e => e.Version));
    }

    [Fact]
    public void Reads_summary_sections_and_wrapped_bullets()
    {
        var entry = Changelog.Parse(Sample)[0];

        Assert.Null(entry.Date);
        Assert.Equal("Big one.", entry.Summary);
        Assert.Equal(["Added", "Fixed"], entry.Sections.Select(s => s.Heading));
        Assert.Equal(["First feature, written over two lines.", "Second feature"], entry.Sections[0].Items);
        Assert.Equal(["A bug"], entry.Sections[1].Items);
    }

    [Fact]
    public void Reads_dates_and_bullets_without_a_subheading()
    {
        var entry = Changelog.Parse(Sample)[1];

        Assert.Equal(new DateOnly(2026, 11, 15), entry.Date);
        Assert.Equal(string.Empty, entry.Sections[0].Heading);
        Assert.Equal(["A bullet with no subheading"], entry.Sections[0].Items);
    }

    [Fact]
    public void Handles_windows_line_endings()
    {
        var entries = Changelog.Parse(Sample.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(2, entries.Count);
        Assert.Equal("A bug", entries[0].Sections[1].Items[0]);
    }

    [Fact]
    public void Between_returns_the_versions_since_the_last_one_seen()
    {
        var entries = Changelog.Parse(Sample);

        var since = Changelog.Between(entries, new AppVersion(0, 9, 0), new AppVersion(0, 10, 0));

        Assert.Equal([new AppVersion(0, 10, 0)], since.Select(e => e.Version));
    }

    // The real files: a version bump without release notes fails the tests (and the release workflow).

    [Fact]
    public void Real_changelog_has_notes_for_the_app_version()
    {
        var root = RepoRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "TrayTranslator.App", "TrayTranslator.App.csproj"));
        var versionText = VersionElement().Match(project).Groups[1].Value;
        Assert.True(AppVersion.TryParse(versionText, out var appVersion), $"<Version> '{versionText}' is not MAJOR.MINOR.PATCH");

        var entries = Changelog.Parse(File.ReadAllText(Path.Combine(root, "CHANGELOG.md")));
        var entry = Changelog.Find(entries, appVersion);

        Assert.True(entry is not null, $"CHANGELOG.md has no '## [{appVersion}]' section");
        Assert.NotEmpty(entry.Sections.SelectMany(s => s.Items));
    }

    [Fact]
    public void Real_changelog_versions_are_unique_and_all_have_items()
    {
        var entries = Changelog.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "CHANGELOG.md")));

        Assert.Equal(entries.Count, entries.Select(e => e.Version).Distinct().Count());
        Assert.All(entries, e => Assert.NotEmpty(e.Sections.SelectMany(s => s.Items)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrayTranslator.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("<Version>([^<]+)</Version>")]
    private static partial Regex VersionElement();
}
