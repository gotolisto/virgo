using System.Globalization;
using System.Windows;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Ui;

public partial class SettingsWindow : Window
{
    private readonly WindowContext _context;

    internal SettingsWindow(WindowContext context)
    {
        _context = context;
        InitializeComponent();
        ThemeMode = context.IsDarkTheme() ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;

        LanguageA.ItemsSource = LanguageCatalog.All;
        LanguageB.ItemsSource = LanguageCatalog.All;
        DefaultEngine.ItemsSource = new[] { new Choice<EngineId>("DeepL", EngineId.DeepL), new Choice<EngineId>("Google", EngineId.Google) };
        Theme.ItemsSource = new[]
        {
            new Choice<ThemePreference>("Follow Windows", ThemePreference.System),
            new Choice<ThemePreference>("Light", ThemePreference.Light),
            new Choice<ThemePreference>("Dark", ThemePreference.Dark),
        };

        DefaultEngine.SelectedValuePath = Theme.SelectedValuePath = "Value";
        DefaultEngine.DisplayMemberPath = Theme.DisplayMemberPath = "Label";

        DeepLKey.Configure("DeepL API key", context.Engines[EngineId.DeepL], Links.DeepLSignUp);
        GoogleKey.Configure("Google Cloud Translation API key", context.Engines[EngineId.Google], Links.GoogleSignUp);

        Load(context.Settings.Current);
    }

    private void Load(AppSettings s)
    {
        LanguageA.SelectedValue = s.LanguageA;
        LanguageB.SelectedValue = s.LanguageB;
        DefaultEngine.SelectedValue = s.DefaultEngine;
        Theme.SelectedValue = s.Theme;

        DeepLKey.Key = _context.Secrets.Get(SecretNames.DeepLApiKey) ?? string.Empty;
        GoogleKey.Key = _context.Secrets.Get(SecretNames.GoogleApiKey) ?? string.Empty;

        GoogleCeiling.Text = s.GoogleMonthlyCeiling.ToString(CultureInfo.CurrentCulture);
        AllowPaidUsage.IsChecked = s.AllowPaidUsage;
        Interval.Value = s.DoublePressIntervalMs;
        MaxTextLength.Text = s.MaxTextLength.ToString(CultureInfo.CurrentCulture);
        StartWithWindows.IsChecked = s.StartWithWindows;
        ReplaceAutomatically.IsChecked = s.ReplaceAutomatically;
        CheckForUpdates.IsChecked = s.CheckForUpdatesAutomatically;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        IntervalText.Text = string.Create(CultureInfo.CurrentCulture, $"{(int)e.NewValue} ms");

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var a = LanguageA.SelectedValue as string;
        var b = LanguageB.SelectedValue as string;
        if (a is null || b is null || a == b)
        {
            ErrorText.Text = "Choose two different languages.";
            return;
        }

        if (!long.TryParse(GoogleCeiling.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var ceiling) || ceiling < 0)
        {
            ErrorText.Text = "The Google ceiling must be a whole number of characters.";
            return;
        }

        if (!int.TryParse(MaxTextLength.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var maxLength)
            || maxLength < AppSettings.MinMaxTextLength || maxLength > AppSettings.MaxMaxTextLength)
        {
            ErrorText.Text = string.Create(CultureInfo.CurrentCulture, $"Maximum text length must be between {AppSettings.MinMaxTextLength} and {AppSettings.MaxMaxTextLength:N0}.");
            return;
        }

        if (AllowPaidUsage.IsChecked == true && !_context.Settings.Current.AllowPaidUsage)
        {
            var answer = MessageBox.Show(
                this,
                "With paid usage on, Google keeps translating past the ceiling and bills your Google Cloud account. Turn it on?",
                "Allow paid usage",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _context.Secrets.Set(SecretNames.DeepLApiKey, DeepLKey.Key);
        _context.Secrets.Set(SecretNames.GoogleApiKey, GoogleKey.Key);

        _context.Settings.Save(_context.Settings.Current with
        {
            LanguageA = a,
            LanguageB = b,
            DefaultEngine = DefaultEngine.SelectedValue is EngineId engine ? engine : EngineId.DeepL,
            GoogleMonthlyCeiling = ceiling,
            AllowPaidUsage = AllowPaidUsage.IsChecked == true,
            DoublePressIntervalMs = (int)Interval.Value,
            MaxTextLength = maxLength,
            StartWithWindows = StartWithWindows.IsChecked == true,
            ReplaceAutomatically = ReplaceAutomatically.IsChecked == true,
            CheckForUpdatesAutomatically = CheckForUpdates.IsChecked == true,
            Theme = Theme.SelectedValue is ThemePreference theme ? theme : ThemePreference.System,
        });

        Close();
    }
}

/// <summary>A labelled value for combo boxes.</summary>
internal sealed record Choice<T>(string Label, T Value);
