using System.Text;
using System.Text.RegularExpressions;

namespace TrayTranslator.Core.Versioning;

/// <param name="Heading">"Added", "Fixed", …; empty for lines before the first subheading.</param>
/// <param name="Items">The bullet points, each joined onto one line.</param>
public sealed record ChangelogSection(string Heading, IReadOnlyList<string> Items);

/// <param name="Version">The version this entry describes.</param>
/// <param name="Date">The date after the heading ("2026-11-15"), or null while it reads "Unreleased".</param>
/// <param name="Summary">Plain text between the heading and the first list.</param>
public sealed record ChangelogEntry(AppVersion Version, DateOnly? Date, string Summary, IReadOnlyList<ChangelogSection> Sections);

/// <summary>
/// Reads CHANGELOG.md (Keep a Changelog format): <c>## [x.y.z] - date</c> headings, <c>### Added</c>
/// subheadings and <c>- </c> bullets, with wrapped lines indented under their bullet.
/// </summary>
public static partial class Changelog
{
    /// <summary>Versioned entries, newest first. The Unreleased section is skipped.</summary>
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var entries = new List<ChangelogEntry>();

        ChangelogEntryBuilder? current = null;
        foreach (var rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current?.Build() is { } done)
                {
                    entries.Add(done);
                }

                current = ChangelogEntryBuilder.FromHeading(line);
                continue;
            }

            current?.Add(line);
        }

        if (current?.Build() is { } last)
        {
            entries.Add(last);
        }

        return [.. entries.OrderByDescending(e => e.Version)];
    }

    /// <summary>The entry for <paramref name="version"/>, or null when the changelog has none.</summary>
    public static ChangelogEntry? Find(IReadOnlyList<ChangelogEntry> entries, AppVersion version) =>
        entries.FirstOrDefault(e => e.Version == version);

    /// <summary>Entries newer than <paramref name="since"/> up to and including <paramref name="current"/>.</summary>
    public static IReadOnlyList<ChangelogEntry> Between(IReadOnlyList<ChangelogEntry> entries, AppVersion since, AppVersion current) =>
        [.. entries.Where(e => e.Version > since && e.Version <= current)];

    [GeneratedRegex(@"^##\s+\[(?<version>[^\]]+)\](?:\s*-\s*(?<date>.+))?$")]
    private static partial Regex VersionHeading();

    private sealed class ChangelogEntryBuilder
    {
        private readonly AppVersion _version;
        private readonly DateOnly? _date;
        private readonly StringBuilder _summary = new();
        private readonly List<(string Heading, List<string> Items)> _sections = [];

        private ChangelogEntryBuilder(AppVersion version, DateOnly? date)
        {
            _version = version;
            _date = date;
        }

        /// <summary>Null for "## [Unreleased]" or anything that is not a version heading.</summary>
        public static ChangelogEntryBuilder? FromHeading(string line)
        {
            var match = VersionHeading().Match(line);
            if (!match.Success || !AppVersion.TryParse(match.Groups["version"].Value, out var version))
            {
                return null;
            }

            DateOnly? date = DateOnly.TryParseExact(
                match.Groups["date"].Value.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed)
                ? parsed
                : null;
            return new ChangelogEntryBuilder(version, date);
        }

        public void Add(string line)
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                _sections.Add((line[4..].Trim(), []));
                return;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                if (_sections.Count == 0)
                {
                    _sections.Add((string.Empty, []));
                }

                _sections[^1].Items.Add(line[2..].Trim());
                return;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            // An indented line continues the bullet above it.
            if (line.StartsWith(' ') && _sections.Count > 0 && _sections[^1].Items.Count > 0)
            {
                var items = _sections[^1].Items;
                items[^1] = items[^1] + " " + trimmed;
                return;
            }

            if (_sections.Count == 0)
            {
                _summary.Append(_summary.Length == 0 ? string.Empty : " ").Append(trimmed);
            }
        }

        public ChangelogEntry Build() => new(
            _version,
            _date,
            _summary.ToString(),
            [.. _sections.Select(s => new ChangelogSection(s.Heading, s.Items))]);
    }
}
