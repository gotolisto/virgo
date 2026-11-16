using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TrayTranslator.Core.Versioning;

namespace TrayTranslator.App.Ui;

/// <summary>Every version and what it changed, read from the CHANGELOG.md built into the app.</summary>
public partial class WhatsNewWindow : Window
{
    private const string ResourceName = "TrayTranslator.App.CHANGELOG.md";

    internal WhatsNewWindow(bool dark)
    {
        InitializeComponent();
        ThemeMode = dark ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;

        var entries = Load();
        if (entries.Count == 0)
        {
            Entries.Children.Add(new TextBlock { Text = "No release notes are available.", Opacity = 0.75 });
            return;
        }

        AppVersion? installed = AppVersion.TryParse(AppInfo.Version, out var parsed) ? parsed : null;
        foreach (var entry in entries)
        {
            AddEntry(entry, entry.Version == installed);
        }
    }

    /// <summary>The changelog entries built into this exe, newest first.</summary>
    public static IReadOnlyList<ChangelogEntry> Load()
    {
        using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return Changelog.Parse(reader.ReadToEnd());
    }

    private void AddEntry(ChangelogEntry entry, bool installed)
    {
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };
        title.Inlines.Add(new Run("Version " + entry.Version));
        var when = entry.Date is { } date
            ? date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
            : "not released yet";
        title.Inlines.Add(new Run("   " + when) { FontSize = 12, FontWeight = FontWeights.Normal });
        if (installed)
        {
            title.Inlines.Add(new Run("   (installed)") { FontSize = 12, FontWeight = FontWeights.Normal });
        }

        Entries.Children.Add(title);

        if (entry.Summary.Length > 0)
        {
            Entries.Children.Add(new TextBlock { Text = entry.Summary, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        }

        foreach (var section in entry.Sections)
        {
            if (section.Heading.Length > 0)
            {
                Entries.Children.Add(new TextBlock { Text = section.Heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            }

            foreach (var item in section.Items)
            {
                Entries.Children.Add(Bullet(item));
            }
        }
    }

    /// <summary>A bullet line; **bold** spans in the changelog are shown bold.</summary>
    private static TextBlock Bullet(string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 3, 0, 0) };
        block.Inlines.Add(new Run("•  "));
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            var run = new Run(parts[i]);
            if (i % 2 == 1)
            {
                run.FontWeight = FontWeights.SemiBold;
            }

            block.Inlines.Add(run);
        }

        return block;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
