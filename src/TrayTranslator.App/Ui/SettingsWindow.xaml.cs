using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Input;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Ui;

public partial class SettingsWindow : Window
{
    private readonly WindowContext _context;
    private readonly ObservableCollection<AiActionRow> _aiActions = [];

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

        foreach (var language in LanguageCatalog.All)
        {
            ExtraLanguages.Items.Add(new System.Windows.Controls.CheckBox
            {
                Content = language.Name,
                Tag = language.Code,
                Margin = new Thickness(0, 2, 8, 2),
            });
        }

        DefaultEngine.SelectedValuePath = Theme.SelectedValuePath = "Value";
        DefaultEngine.DisplayMemberPath = Theme.DisplayMemberPath = "Label";

        DeepLKey.Configure("DeepL API key", context.Engines[EngineId.DeepL], Links.DeepLSignUp);
        GoogleKey.Configure("Google Cloud Translation API key", context.Engines[EngineId.Google], Links.GoogleSignUp);
        ClaudeKey.Configure("Claude API key", context.Ai, Links.ClaudeSignUp);
        AiActionRows.ItemsSource = _aiActions;
        _aiActions.CollectionChanged += (_, _) => AddAiAction.IsEnabled = _aiActions.Count < AiActionList.MaxCount;

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
        ClaudeKey.Key = _context.Secrets.Get(SecretNames.AnthropicApiKey) ?? string.Empty;

        // A model id typed into the settings file by hand stays selectable.
        AiModel.ItemsSource = AiModels.All.Any(m => m.Id == s.AiModel)
            ? AiModels.All
            : [.. AiModels.All, new AiModel(s.AiModel, s.AiModel)];
        AiModel.SelectedValue = s.AiModel;
        _aiActions.Clear();
        foreach (var action in s.AiActions)
        {
            _aiActions.Add(AiActionRow.From(action));
        }

        GoogleCeiling.Text = s.GoogleMonthlyCeiling.ToString(CultureInfo.CurrentCulture);
        AllowPaidUsage.IsChecked = s.AllowPaidUsage;
        Interval.Value = s.DoublePressIntervalMs;
        MaxTextLength.Text = s.MaxTextLength.ToString(CultureInfo.CurrentCulture);
        StartWithWindows.IsChecked = s.StartWithWindows;
        ReplaceAutomatically.IsChecked = s.ReplaceAutomatically;
        foreach (var box in ExtraLanguageBoxes)
        {
            box.IsChecked = s.ExtraLanguages.Contains((string)box.Tag);
        }
        CheckForUpdates.IsChecked = s.CheckForUpdatesAutomatically;
    }

    private IEnumerable<System.Windows.Controls.CheckBox> ExtraLanguageBoxes =>
        ExtraLanguages.Items.OfType<System.Windows.Controls.CheckBox>();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnAddAiActionClick(object sender, RoutedEventArgs e)
    {
        // Start on a key no other action uses.
        var used = _aiActions.Select(r => r.Shortcut).ToHashSet();
        var key = Shortcut.Keys.FirstOrDefault(k => !used.Contains(new Shortcut(ModifierKeys.Alt, k)) && k != 'C', 'A');
        _aiActions.Add(new AiActionRow { Name = "New action", Key = key, Instruction = string.Empty });
    }

    private void OnRemoveAiActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiActionRow row })
        {
            _aiActions.Remove(row);
        }
    }

    /// <summary>The first problem that would stop an active AI action from working, or null.</summary>
    private string? CheckAiActions()
    {
        var active = _aiActions.Where(r => r.Enabled).ToList();
        foreach (var row in active)
        {
            if (string.IsNullOrWhiteSpace(row.Instruction))
            {
                return $"Write an instruction for {row.DisplayName}, or switch it off.";
            }

            if (row.Shortcut.IsTakenByTranslation)
            {
                return $"Ctrl with C is the translation shortcut. Pick another key for {row.DisplayName}.";
            }
        }

        var clash = active.GroupBy(r => r.Shortcut).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            return $"{clash.Key.ToGestureString()} is used by two active actions. Change one of them.";
        }

        if (active.Count > 0 && string.IsNullOrWhiteSpace(ClaudeKey.Key))
        {
            return "Enter your Claude API key to use AI actions, or switch them off.";
        }

        return null;
    }

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

        if (CheckAiActions() is { } aiProblem)
        {
            ErrorText.Text = aiProblem;
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
        _context.Secrets.Set(SecretNames.AnthropicApiKey, ClaudeKey.Key);

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
            ExtraLanguages = new LanguageList(ExtraLanguageBoxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag)),
            CheckForUpdatesAutomatically = CheckForUpdates.IsChecked == true,
            Theme = Theme.SelectedValue is ThemePreference theme ? theme : ThemePreference.System,
            AiActions = new AiActionList(_aiActions.Select(r => r.ToAction())),
            AiModel = AiModel.SelectedValue as string ?? AiModels.DefaultId,
        });

        Close();
    }
}

/// <summary>A labelled value for combo boxes.</summary>
internal sealed record Choice<T>(string Label, T Value);
